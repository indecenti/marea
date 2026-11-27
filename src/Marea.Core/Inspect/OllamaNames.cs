using System.Text.Json;

namespace Marea.Core;

/// <summary>A local Ollama tag: where it was pulled from and how the user calls it.</summary>
/// <param name="Host">Registry host (registry.ollama.ai, hf.co).</param>
/// <param name="Repository">Repository path on the registry (library/llama3.2).</param>
/// <param name="Tag">Tag (3b, latest).</param>
/// <param name="Name">Name as the Ollama CLI shows it (llama3.2:3b).</param>
public sealed record OllamaTag(string Host, string Repository, string Tag, string Name);

/// <summary>Maps Ollama model blobs to the tags that use them, from local manifests.</summary>
public static class OllamaNames
{
    /// <summary>Model-layer digest (sha256 hex) → model names, for the manifests under <paramref name="modelsDir"/>.</summary>
    public static Dictionary<string, List<string>> Read(string modelsDir) =>
        ReadTags(modelsDir).ToDictionary(kv => kv.Key, kv => kv.Value.Select(t => t.Name).ToList(), StringComparer.Ordinal);

    /// <summary>Layers that are part of a model's weights (not license, template or parameters).</summary>
    private static readonly HashSet<string> WeightTypes =
    [
        "application/vnd.ollama.image.model", "application/vnd.ollama.image.projector", "application/vnd.ollama.image.adapter",
        "application/vnd.ollama.image.draft",
    ];

    /// <summary>Model-layer digest (sha256 hex) → local tags using it, sorted by name. Blobs Ollama converted
    /// on this PC are named after the tag the user pulled, never after the hidden <c>llamacpp:</c> tag.</summary>
    public static Dictionary<string, List<OllamaTag>> ReadTags(string modelsDir)
    {
        var tags = new Dictionary<string, List<OllamaTag>>(StringComparer.Ordinal);
        var root = Path.Combine(modelsDir, "manifests");
        if (!Directory.Exists(root))
        {
            return tags;
        }
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root, "*", opts))
        {
            var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            if (parts.Length < 4)
            {
                continue;
            }
            var tag = new OllamaTag(parts[0], string.Join('/', parts[1..^1]), parts[^1], Display(parts));
            if (OllamaLayout.IsShadow(tag.Host, tag.Repository, tag.Tag))
            {
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("layers", out var layers))
                {
                    continue;
                }
                foreach (var layer in layers.EnumerateArray())
                {
                    var media = layer.TryGetProperty("mediaType", out var m) ? m.GetString() : null;
                    var digest = layer.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                    if (media is not null && WeightTypes.Contains(media) && digest.StartsWith("sha256:", StringComparison.Ordinal))
                    {
                        Add(tags, digest[7..], tag);
                    }
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // An unreadable manifest only means one name fewer.
            }
        }
        foreach (var (hex, child) in OllamaLayout.Read(modelsDir).Children)
        {
            if (!tags.ContainsKey(hex))
            {
                Add(tags, hex, child.Parent);
            }
        }
        foreach (var list in tags.Values)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        return tags;
    }

    private static void Add(Dictionary<string, List<OllamaTag>> tags, string hex, OllamaTag tag)
    {
        if (!tags.TryGetValue(hex, out var list))
        {
            tags[hex] = list = [];
        }
        if (!list.Contains(tag))
        {
            list.Add(tag);
        }
    }

    /// <summary>host/namespace/model/tag → the short name the Ollama CLI shows.</summary>
    internal static string Display(string[] parts)
    {
        var tag = parts[^1];
        var model = parts[^2];
        var ns = string.Join('/', parts[1..^2]);
        return parts[0] == "registry.ollama.ai"
            ? ns == "library" ? $"{model}:{tag}" : $"{ns}/{model}:{tag}"
            : $"{parts[0]}/{ns}/{model}:{tag}";
    }
}
