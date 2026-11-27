using System.Text.RegularExpressions;

namespace Marea.Core;

/// <summary>Where a model's files came from, as far as the folder layout tells.</summary>
public enum ModelKind
{
    /// <summary>A single model file (checkpoint, LoRA, GGUF...).</summary>
    File,
    /// <summary>A folder with config.json or model_index.json (transformers, diffusers).</summary>
    Folder,
    /// <summary>A repository in the Hugging Face cache (models--org--name).</summary>
    HuggingFace,
    /// <summary>A model pulled with Ollama.</summary>
    Ollama,
}

/// <summary>Whether a model's bytes are on disk.</summary>
public enum ModelResidency
{
    /// <summary>Every file is on disk.</summary>
    OnDisk,
    /// <summary>Some files are evaporated.</summary>
    Partly,
    /// <summary>Every file is evaporated.</summary>
    Evaporated,
}

/// <summary>One model: the files that belong together, and what their headers say.</summary>
public sealed class ModelEntry
{
    /// <summary>Stable grouping key.</summary>
    public required string Key { get; init; }
    /// <summary>Name people recognise (repo id, Ollama tag, folder or file name). Untrusted text.</summary>
    public required string Name { get; init; }
    /// <summary>How the files were grouped.</summary>
    public required ModelKind Kind { get; init; }
    /// <summary>Folder holding the model.</summary>
    public required string Folder { get; init; }
    /// <summary>Files of the model, largest first.</summary>
    public required List<FileRecord> Files { get; init; }
    /// <summary>Metadata of each file (same order as <see cref="Files"/>); null when not inspected yet.</summary>
    public required List<ModelMeta?> Meta { get; init; }
    /// <summary>What Hugging Face's scanners say about its files: the worst level, or null when unknown.</summary>
    public string? HubScan { get; init; }

    /// <summary>Content hashes Ollama keeps only to go back to an older version of itself.</summary>
    public IReadOnlySet<string> RollbackShas { get; init; } = new HashSet<string>();

    private bool HubFlagged => HubScan is not (null or "clean");

    /// <summary>Files Ollama keeps only for a downgrade (see <see cref="OllamaLayout"/>).</summary>
    public List<FileRecord> RollbackFiles => Files.Where(f => RollbackShas.Contains(f.Sha256)).ToList();

    /// <summary>False when every file was inspected and none is a model (datasets, archives...).</summary>
    public bool IsModel => Meta.Any(m => m is null || m.Format != "data");
    /// <summary>Last time any of its files was used (read or written).</summary>
    public DateTime LastUsedUtc => Files.Max(f => f.LastAccessUtc > new DateTime(f.MTimeTicks, DateTimeKind.Utc) ? f.LastAccessUtc : new DateTime(f.MTimeTicks, DateTimeKind.Utc));
    /// <summary>True when no file of the model was used for <paramref name="days"/> days.</summary>
    public bool UnusedFor(int days, DateTime nowUtc) => nowUtc - LastUsedUtc >= TimeSpan.FromDays(days);
    /// <summary>Total logical size.</summary>
    public long Size => Files.Sum(f => f.Size);
    /// <summary>Bytes currently evaporated.</summary>
    public long EvaporatedBytes => Files.Where(f => f.Residency == Marea.Core.Residency.Evaporated).Sum(f => f.Size);
    /// <summary>Residency of the whole model.</summary>
    public ModelResidency State => EvaporatedBytes == 0 ? ModelResidency.OnDisk : EvaporatedBytes == Size ? ModelResidency.Evaporated : ModelResidency.Partly;
    /// <summary>Formats present, largest share first (data files excluded).</summary>
    public List<string> Formats => Pairs().Where(p => p.M.Format != "data").GroupBy(p => p.M.Format)
        .OrderByDescending(g => g.Sum(p => p.F.Size)).Select(g => g.Key).ToList();
    /// <summary>Quantizations or tensor types present.</summary>
    public List<string> Precisions => Pairs().Select(p => p.M.Precision).OfType<string>().Distinct().ToList();
    /// <summary>Declared architecture, if any file declares one.</summary>
    public string? Architecture => Pairs().Select(p => p.M.Architecture).FirstOrDefault(a => a is not null);
    /// <summary>Weights in the largest format (formats of the same model are not added up).</summary>
    public long Parameters => Pairs().GroupBy(p => p.M.Format).Select(g => g.DistinctBy(p => p.F.Sha256).Sum(p => p.M.Parameters))
        .DefaultIfEmpty(0).Max();
    /// <summary>Worst safety verdict of its files.</summary>
    /// <remarks>A Hugging Face flag alone raises it to <see cref="SafetyVerdict.Suspicious"/>, never to
    /// dangerous: hub scanners report many false positives on pickles.</remarks>
    public SafetyVerdict Safety
    {
        get
        {
            var own = Pairs().Select(p => p.M.Safety).DefaultIfEmpty(SafetyVerdict.NotAModel).Max();
            return HubFlagged && own < SafetyVerdict.Suspicious ? SafetyVerdict.Suspicious : own;
        }
    }
    /// <summary>Safety findings of its files, plus the Hugging Face flag if any.</summary>
    public List<string> Findings => Pairs().SelectMany(p => p.M.Findings)
        .Concat(HubFlagged ? ["Hugging Face scan: " + HubScan] : []).Distinct().ToList();

    private IEnumerable<(FileRecord F, ModelMeta M)> Pairs() =>
        Files.Zip(Meta).Where(p => p.Second is not null).Select(p => (p.First, p.Second!));
}

/// <summary>Groups tracked files into models: Hugging Face cache repos, Ollama models, model folders,
/// sharded checkpoints and single files.</summary>
public static partial class ModelCatalog
{
    [GeneratedRegex(@"[\\/]models--([^\\/]+?)--([^\\/]+)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex HfCache();

    [GeneratedRegex(@"^(.*?)[-_.]?\d{5}-of-\d{5}", RegexOptions.IgnoreCase)]
    private static partial Regex Shard();

    // File names that say nothing about the model: the folder name is used instead.
    [GeneratedRegex(@"^(model|pytorch_model|diffusion_pytorch_model|consolidated|weights|adapter_model|ggml-model|unet|vae)([-_.].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Generic();

    /// <summary>Builds the model list. <paramref name="folderHasFile"/> is injectable for tests.</summary>
    public static List<ModelEntry> Build(IEnumerable<FileRecord> files, IReadOnlyDictionary<string, ModelMeta> meta,
        Func<string, string, bool>? folderHasFile = null, IReadOnlyDictionary<string, string>? hubScans = null)
    {
        folderHasFile ??= (dir, name) => File.Exists(Path.Combine(dir, name));
        var ollama = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var layouts = new Dictionary<string, OllamaLayout>(StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> Rollback(string modelsDir) =>
            (layouts.TryGetValue(modelsDir, out var l) ? l : layouts[modelsDir] = OllamaLayout.Read(modelsDir)).Rollback;
        var groups = new Dictionary<string, (string Name, ModelKind Kind, string Folder, List<FileRecord> Files)>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var (key, name, kind, folder) = Classify(f, folderHasFile, ollama);
            if (!groups.TryGetValue(key, out var g))
            {
                groups[key] = g = (name, kind, folder, []);
            }
            g.Files.Add(f);
        }
        return groups.Select(kv =>
        {
            var list = kv.Value.Files.OrderByDescending(f => f.Size).ToList();
            return new ModelEntry
            {
                Key = kv.Key,
                Name = kv.Value.Name,
                Kind = kv.Value.Kind,
                Folder = kv.Value.Folder,
                Files = list,
                Meta = list.Select(f => meta.GetValueOrDefault(f.Sha256)).ToList(),
                HubScan = WorstScan(list.Select(f => hubScans?.GetValueOrDefault(f.Sha256))),
                RollbackShas = kv.Value.Kind == ModelKind.Ollama ? Rollback(kv.Value.Folder) : OllamaLayout.Empty.Rollback,
            };
        }).OrderByDescending(m => m.Size).ToList();
    }

    /// <summary>unsafe beats caution beats any other flag beats clean; null when nothing is known.</summary>
    private static string? WorstScan(IEnumerable<string?> levels)
    {
        var known = levels.OfType<string>().ToList();
        return known.Count == 0 ? null
            : known.FirstOrDefault(l => l == "unsafe") ?? known.FirstOrDefault(l => l == "caution")
              ?? known.FirstOrDefault(l => l != "clean") ?? "clean";
    }

    private static (string Key, string Name, ModelKind Kind, string Folder) Classify(FileRecord f,
        Func<string, string, bool> has, Dictionary<string, Dictionary<string, List<string>>> ollama)
    {
        var dir = Path.GetDirectoryName(f.Path) ?? f.Root;
        if (HfCache().Match(f.Path) is { Success: true } hf)
        {
            var repo = $"{hf.Groups[1].Value}/{hf.Groups[2].Value}";
            var repoDir = f.Path[..(hf.Index + hf.Length - 1)];
            return ("hf:" + repo, repo, ModelKind.HuggingFace, repoDir);
        }
        var fileName = Path.GetFileName(f.Path);
        if (fileName.StartsWith("sha256-", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(dir), "blobs", StringComparison.OrdinalIgnoreCase))
        {
            var modelsDir = Path.GetDirectoryName(dir)!;
            if (!ollama.TryGetValue(modelsDir, out var names))
            {
                ollama[modelsDir] = names = OllamaNames.Read(modelsDir);
            }
            if (names.TryGetValue(fileName[7..], out var tags))
            {
                return ("ollama:" + tags[0], string.Join(", ", tags), ModelKind.Ollama, modelsDir);
            }
        }
        for (var d = dir; d is not null && d.Length >= f.Root.Length; d = Path.GetDirectoryName(d))
        {
            if (has(d, "model_index.json"))
            {
                return ("dir:" + d, Path.GetFileName(d), ModelKind.Folder, d);
            }
        }
        if (has(dir, "config.json"))
        {
            return ("dir:" + dir, Path.GetFileName(dir), ModelKind.Folder, dir);
        }
        if (Shard().Match(fileName) is { Success: true } shard)
        {
            var stem = shard.Groups[1].Value.Length > 0 ? shard.Groups[1].Value : Path.GetFileName(dir);
            return ("shard:" + Path.Combine(dir, stem), stem, ModelKind.File, dir);
        }
        var fileStem = Path.GetFileNameWithoutExtension(fileName);
        var name = Generic().IsMatch(fileStem) && !string.Equals(dir, Path.TrimEndingDirectorySeparator(f.Root), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(dir)
            : fileStem;
        return ("file:" + f.Path, name, ModelKind.File, dir);
    }
}
