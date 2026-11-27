using System.Text.Json;

namespace Marea.Core;

/// <summary>A blob listed by a child manifest of a migrated Ollama model.</summary>
/// <param name="Parent">The tag the user pulled (qwen3.5:9b), not the hidden shadow tag.</param>
/// <param name="Size">Size in bytes, from the child manifest.</param>
public sealed record OllamaChildBlob(OllamaTag Parent, long Size);

/// <summary>What Ollama's 0.40 model migration left in a models folder.</summary>
/// <remarks>
/// A migrated tag manifest keeps its original layers (the "legacy anchor", for a downgrade) and adds a
/// manifest list whose children name a runner: <c>ggml</c> (the original weights) and <c>llamacpp</c>
/// (weights converted on this PC, which current Ollama loads). A hidden <c>llamacpp:&lt;digest&gt;</c>
/// tag repeats the converted child. The original weights are then needed only to go back to an older
/// Ollama: those are the rollback copies.
/// </remarks>
/// <param name="Children">Blobs of child manifests (sha256 hex) → the tag they belong to.</param>
/// <param name="Rollback">Blobs kept only for a downgrade: used by no current child and no other tag.</param>
public sealed record OllamaLayout(IReadOnlyDictionary<string, OllamaChildBlob> Children, IReadOnlySet<string> Rollback)
{
    private const string ListType = "application/vnd.ollama.manifest.list.v2+json";
    private const long MaxManifestBytes = 1 << 20;

    /// <summary>A layout with nothing migrated.</summary>
    public static readonly OllamaLayout Empty = new(new Dictionary<string, OllamaChildBlob>(), new HashSet<string>());

    /// <summary>True for the hidden tag Ollama writes for a converted model (library/llamacpp:&lt;64 hex&gt;).</summary>
    public static bool IsShadow(string host, string repository, string tag) =>
        host == "registry.ollama.ai" && repository == "library/llamacpp" && OllamaBlob.Hex64().IsMatch(tag);

    /// <summary>True for layers that are manifests themselves, not data (lists, child manifests).</summary>
    public static bool IsManifestLayer(string? mediaType) =>
        mediaType is not null && mediaType.Contains("manifest", StringComparison.Ordinal);

    /// <summary>Reads every tag manifest under <paramref name="modelsDir"/>; never throws on bad files.</summary>
    public static OllamaLayout Read(string modelsDir)
    {
        var root = Path.Combine(modelsDir, "manifests");
        if (!Directory.Exists(root))
        {
            return Empty;
        }
        var children = new Dictionary<string, OllamaChildBlob>(StringComparer.Ordinal);
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var inUse = new HashSet<string>(StringComparer.Ordinal);
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root, "*", opts))
        {
            var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            if (parts.Length < 4)
            {
                continue;
            }
            var tag = new OllamaTag(parts[0], string.Join('/', parts[1..^1]), parts[^1], OllamaNames.Display(parts));
            if (IsShadow(tag.Host, tag.Repository, tag.Tag) || ReadJson(file) is not { } manifest)
            {
                continue;
            }
            var own = DataLayers(manifest).Select(l => l.Hex).ToList();
            var list = Layers(manifest).FirstOrDefault(l => l.Type == ListType);
            var kids = list.Hex is null ? [] : ReadChildren(modelsDir, list.Hex);
            if (!kids.Any(k => k.Runner == "llamacpp"))
            {
                inUse.UnionWith(own); // not migrated: every layer is what Ollama loads
                continue;
            }
            candidates.UnionWith(own);
            foreach (var (runner, layers) in kids)
            {
                foreach (var (hex, size) in layers)
                {
                    children.TryAdd(hex, new OllamaChildBlob(tag, size));
                    if (runner == "ggml")
                    {
                        candidates.Add(hex);
                    }
                    else
                    {
                        inUse.Add(hex);
                    }
                }
            }
        }
        candidates.ExceptWith(inUse);
        return new OllamaLayout(children, candidates);
    }

    /// <summary>Children of a manifest list blob: runner and data layers of each child manifest.</summary>
    private static List<(string Runner, List<(string Hex, long Size)> Layers)> ReadChildren(string modelsDir, string listHex)
    {
        var result = new List<(string, List<(string, long)>)>();
        if (ReadJson(BlobPath(modelsDir, listHex)) is not { } list
            || !list.TryGetProperty("manifests", out var manifests) || manifests.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var child in manifests.EnumerateArray())
        {
            var runner = child.TryGetProperty("runner", out var r) ? r.GetString() ?? "" : "";
            if (Hex(child) is { } hex && ReadJson(BlobPath(modelsDir, hex)) is { } doc)
            {
                result.Add((runner, DataLayers(doc).ToList()));
            }
        }
        return result;
    }

    private static IEnumerable<(string Hex, long Size)> DataLayers(JsonElement manifest) =>
        Layers(manifest).Where(l => l.Hex is not null && !IsManifestLayer(l.Type)).Select(l => (l.Hex!, l.Size));

    private static IEnumerable<(string? Type, string? Hex, long Size)> Layers(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var l in layers.EnumerateArray())
        {
            var type = l.TryGetProperty("mediaType", out var m) ? m.GetString() : null;
            var size = l.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
            yield return (type, Hex(l), size);
        }
    }

    private static string? Hex(JsonElement e)
    {
        var digest = e.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
        var hex = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest[7..] : "";
        return OllamaBlob.Hex64().IsMatch(hex) ? hex : null;
    }

    private static string BlobPath(string modelsDir, string hex) => Path.Combine(modelsDir, "blobs", "sha256-" + hex);

    /// <summary>Parses a small JSON file; null when missing, too big, evaporated-and-unreadable or malformed.</summary>
    private static JsonElement? ReadJson(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > MaxManifestBytes)
            {
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
