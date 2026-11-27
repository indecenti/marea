using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marea.Core;

/// <summary>A blob referenced by a local Ollama manifest, re-obtainable from its registry by digest.</summary>
/// <param name="Host">Registry host (registry.ollama.ai or hf.co).</param>
/// <param name="Repository">Repository path, e.g. library/llama3 or LiquidAI/LFM2-350M-GGUF.</param>
/// <param name="Sha256">Content digest (lower-case hex).</param>
/// <param name="Size">Size in bytes.</param>
public sealed partial record OllamaBlob(string Host, string Repository, string Sha256, long Size)
{
    /// <summary>Ledger locator: <c>host/repository@sha256:digest</c>.</summary>
    public string Locator => $"{Host}/{Repository}@sha256:{Sha256}";

    /// <summary>Parses a locator produced by <see cref="Locator"/>.</summary>
    public static OllamaBlob Parse(string locator, long size)
    {
        var at = locator.IndexOf("@sha256:", StringComparison.Ordinal);
        var slash = locator.IndexOf('/');
        return new OllamaBlob(locator[..slash], locator[(slash + 1)..at], locator[(at + 8)..], size);
    }

    /// <summary>OCI blob URL on the registry.</summary>
    public Uri BlobUri => new($"https://{Host}/v2/{Repository}/blobs/sha256:{Sha256}");

    [GeneratedRegex("^[a-f0-9]{64}$")]
    internal static partial Regex Hex64();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*(/[A-Za-z0-9][A-Za-z0-9_.\-]*)+$")]
    internal static partial Regex RepoPath();
}

/// <summary>Reads local Ollama manifests and fetches blobs from the public registries, by digest.
/// Allow-list: registry.ollama.ai, its blob storage host, and Hugging Face for hf.co models.</summary>
public sealed partial class OllamaRegistry : IDisposable
{
    /// <summary>Cloudflare R2 bucket host the Ollama registry redirects blob downloads to.</summary>
    public const string OllamaBlobHost = "dd20bb891979d25aebc8bec07b2b3bbc.r2.cloudflarestorage.com";

    private static readonly HashSet<string> SupportedHosts = ["registry.ollama.ai", "hf.co"];
    private readonly HttpClient _http = SafeHttp.CreateClient();

    /// <summary>True for the registry, its exact blob storage host, and Hugging Face hosts.</summary>
    public static bool IsAllowedHost(Uri u) =>
        u.Host is "registry.ollama.ai" or OllamaBlobHost || HuggingFaceClient.IsAllowedHost(u);

    /// <summary>The Ollama models folder: OLLAMA_MODELS or %USERPROFILE%\.ollama\models.</summary>
    public static string DefaultModelsDir =>
        Environment.GetEnvironmentVariable("OLLAMA_MODELS") is { Length: > 0 } env
            ? env
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");

    /// <summary>Every blob referenced by a manifest under <paramref name="modelsDir"/> (unsupported hosts skipped).</summary>
    public static List<OllamaBlob> ReadManifests(string modelsDir)
    {
        var root = Path.Combine(modelsDir, "manifests");
        var blobs = new List<OllamaBlob>();
        if (!Directory.Exists(root))
        {
            return blobs;
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            if (parts.Length < 3 || !SupportedHosts.Contains(parts[0]))
            {
                continue;
            }
            var repo = string.Join('/', parts[1..^1]);
            if (!OllamaBlob.RepoPath().IsMatch(repo) || OllamaLayout.IsShadow(parts[0], repo, parts[^1]))
            {
                continue; // a shadow tag lists weights Ollama converted on this PC: no registry has them
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var layers = doc.RootElement.TryGetProperty("layers", out var l) ? l.EnumerateArray().ToList() : [];
                if (doc.RootElement.TryGetProperty("config", out var cfg))
                {
                    layers.Add(cfg);
                }
                foreach (var layer in layers)
                {
                    if (OllamaLayout.IsManifestLayer(layer.TryGetProperty("mediaType", out var mt) ? mt.GetString() : null))
                    {
                        continue;
                    }
                    var digest = layer.GetProperty("digest").GetString() ?? "";
                    var hex = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest[7..] : "";
                    if (OllamaBlob.Hex64().IsMatch(hex))
                    {
                        blobs.Add(new OllamaBlob(parts[0], repo, hex, layer.GetProperty("size").GetInt64()));
                    }
                }
            }
            catch (Exception e) when (e is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
            {
                // A manifest we cannot read is simply not a source.
            }
        }
        return blobs;
    }

    /// <summary>Confirms the registry still serves the blob (redirect to storage or 200).</summary>
    public async Task<bool> VerifyAsync(OllamaBlob b, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SafeHttp.SendAsync(_http, HttpMethod.Head, b.BlobUri, null, IsAllowedHost, ct, followRedirects: false);
            return resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.TemporaryRedirect or HttpStatusCode.Found
                or HttpStatusCode.SeeOther or HttpStatusCode.PermanentRedirect;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Random-access reader over the blob (HTTP range requests through the allow-list).</summary>
    public IRangeSource Open(OllamaBlob b) => new HttpRangeSource(async (from, ct) =>
    {
        var resp = await SafeHttp.SendAsync(_http, HttpMethod.Get, b.BlobUri, new RangeHeaderValue(from, b.Size - 1), IsAllowedHost, ct);
        if (resp.StatusCode != HttpStatusCode.PartialContent && !(from == 0 && resp.StatusCode == HttpStatusCode.OK))
        {
            resp.Dispose();
            throw new HttpRequestException(Loc.T("hub.status", (int)resp.StatusCode, b.Repository));
        }
        return await resp.Content.ReadAsStreamAsync(ct);
    }, b.Size);

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
