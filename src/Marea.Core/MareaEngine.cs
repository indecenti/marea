using Marea.CloudFiles;

namespace Marea.Core;

/// <summary>Space situation of one volume that holds managed folders.</summary>
/// <param name="Volume">Drive root, e.g. C:\.</param>
/// <param name="FreeBytes">Free bytes.</param>
/// <param name="TotalBytes">Volume size.</param>
/// <param name="TargetBytes">Free space the tide keeps.</param>
public sealed record VolumeStatus(string Volume, long FreeBytes, long TotalBytes, long TargetBytes)
{
    /// <summary>Bytes missing to reach the target (0 when above it).</summary>
    public long Deficit => Math.Max(0, TargetBytes - FreeBytes);
}

/// <summary>Facade used by the desktop app and the CLI: one place that orchestrates scanning,
/// sources, the safety gate, the tide and the provider connections.</summary>
public sealed partial class MareaEngine : IDisposable
{
    private readonly List<ProviderConnection> _connections = [];

    /// <summary>Data folder (settings, ledger).</summary>
    public string DataDir { get; }
    /// <summary>Current settings (call <see cref="SaveSettings"/> after changing them).</summary>
    public MareaSettings Settings { get; }
    /// <summary>The provenance ledger.</summary>
    public Ledger Ledger { get; }
    /// <summary>Watches which programs bring evaporated files back; blocks the ones the user chose.</summary>
    public ReaderGuard Guard { get; }

    /// <summary>Hydration service (subscribe to its Progress for the UI).</summary>
    public HydrationService Hydration { get; }

    private readonly HuggingFaceClient _hub = new();
    private readonly OllamaRegistry _ollama = new();
    private readonly SafetyGate _gate;

    /// <summary>Opens the engine on <paramref name="dataDir"/> (default %LOCALAPPDATA%\Marea).</summary>
    public MareaEngine(string? dataDir = null)
    {
        DataDir = dataDir ?? MareaSettings.DefaultDirectory;
        Settings = MareaSettings.Load(DataDir);
        Ledger = new Ledger(Path.Combine(DataDir, "marea.db"));
        Guard = new ReaderGuard(Settings);
        Hydration = new HydrationService(Ledger, _hub, _ollama, Guard);
        _gate = new SafetyGate(Ledger, Settings);
    }

    /// <summary>Persists settings.</summary>
    public void SaveSettings() => Settings.Save(DataDir);

    /// <summary>Adds a managed folder after validating it; returns the reason on refusal.</summary>
    public string? AddRoot(string path)
    {
        var why = FileSystemFacts.RejectRoot(path, Settings.ManagedRoots);
        if (why is not null)
        {
            return why;
        }
        Settings.ManagedRoots.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
        SaveSettings();
        Ledger.Log("j.root_added", path, 0, true);
        return null;
    }

    /// <summary>Stops managing a folder: brings every file back first. Returns the reason on refusal.</summary>
    public async Task<string?> RemoveRootAsync(string root, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var problem = await BringBackAsync(Ledger.Files().Where(f => string.Equals(f.Root, root, StringComparison.OrdinalIgnoreCase)), progress, ct);
        if (problem is not null)
        {
            return problem;
        }
        if (TryRelease(root) is { } stuck)
        {
            return stuck;
        }
        Settings.ManagedRoots.RemoveAll(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        foreach (var f in Ledger.Files().Where(f => string.Equals(f.Root, root, StringComparison.OrdinalIgnoreCase)))
        {
            Ledger.Remove(f.Path);
        }
        Ledger.Log("j.root_removed", root, 0, true);
        return null;
    }

    /// <summary>Scans managed folders (hashing only what changed).</summary>
    public Task<int> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default) =>
        new Scanner(Ledger, Settings).ScanAsync(progress, ct);

    /// <summary>Looks for sources (Hub, mirror, recipes) and reclassifies.</summary>
    public Task RefreshSourcesAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default) =>
        new SourceResolver(Ledger, Settings, _hub, _ollama).RefreshAsync(progress, ct);

    /// <summary>Verdict for every local file.</summary>
    public List<GateVerdict> Evaluate() => _gate.EvaluateAll(DateTime.UtcNow);

    /// <summary>The tide's plan to free <paramref name="bytes"/> (everything allowed when 0).</summary>
    public List<PlanItem> Plan(long bytes, bool instantOnly = false) =>
        new TidePlanner(_gate, Ledger).Plan(bytes, DateTime.UtcNow, instantOnly);

    /// <summary>Ollama rollback copies still on disk (see <see cref="OllamaLayout"/>).</summary>
    public List<FileRecord> OllamaRollbackFiles() =>
        Models().SelectMany(m => m.RollbackFiles).Where(f => f.Residency == Residency.Local).DistinctBy(f => f.Path).ToList();

    /// <summary>What the quick wizard can offer right now: bytes that come back instantly
    /// (local copy or recipe, offline) and bytes including mirror / download sources.</summary>
    public (long Instant, long All) QuickAvailability() =>
        (Plan(0, instantOnly: true).Sum(p => p.File.Size), Plan(0).Sum(p => p.File.Size));

    /// <summary>The most recent batch that still has evaporated files, with its file count and bytes.</summary>
    public (string Id, int Files, long Bytes, DateTime AtUtc)? LastUndoableBatch()
    {
        var gone = Ledger.Files().Where(f => f.Residency == Residency.Evaporated && f.Batch is not null).ToList();
        var last = gone.GroupBy(f => f.Batch!).OrderByDescending(g => g.Max(f => f.EvaporatedUtc ?? default)).FirstOrDefault();
        return last is null ? null : (last.Key, last.Count(), last.Sum(f => f.Size), last.Max(f => f.EvaporatedUtc ?? default));
    }

    /// <summary>Undo: brings back exactly the files of one evaporation batch. Returns an error or null.</summary>
    public async Task<string?> UndoBatchAsync(string batch, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var files = Ledger.Files().Where(f => f.Batch == batch && f.Residency == Residency.Evaporated).ToList();
        var problem = await BringBackAsync(files, progress, ct);
        if (problem is null)
        {
            Ledger.Log("j.batch_undone", "", files.Sum(f => f.Size), true, Loc.Code("eng.batch_detail", files.Count));
        }
        return problem;
    }

    /// <summary>Evaporates the given files one by one; returns how many left the disk and why others stayed.</summary>
    public async Task<(int Done, List<(FileRecord File, string Why)> Kept)> EvaporateAsync(
        IEnumerable<FileRecord> files, IProgress<ScanProgress>? progress = null, CancellationToken ct = default,
        string? batch = null)
    {
        batch ??= DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var ev = new Evaporator(Ledger, Settings, _hub, _ollama, _gate);
        var done = 0;
        var kept = new List<(FileRecord, string)>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var fresh = Ledger.Get(f.Path) ?? f;
            var why = await ev.EvaporateAsync(fresh, progress, ct, batch);
            if (why is null)
            {
                done++;
                EnsureConnected(fresh.Root);
            }
            else
            {
                kept.Add((fresh, why));
            }
        }
        return (done, kept);
    }

    /// <summary>Brings files back to the disk now (reads them through the provider). Returns an error or null.</summary>
    public async Task<string?> BringBackAsync(IEnumerable<FileRecord> files, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var todo = files.Where(f => File.Exists(f.Path) && Placeholder.GetState(f.Path) != PlaceholderState.NormalFile).ToList();
        if (SpaceProblem(todo.Where(f => Placeholder.GetState(f.Path) == PlaceholderState.Dehydrated)) is { } noSpace)
        {
            return noSpace;
        }
        foreach (var f in todo)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(Loc.T("phase.bring_back"), f.Path, 0, f.Size));
            try
            {
                if (Placeholder.GetState(f.Path) == PlaceholderState.Dehydrated)
                {
                    await Task.Run(() => Placeholder.Hydrate(f.Path), ct);
                }
                await RevertWhenFreeAsync(f.Path, ct);
                var sha = await ContentHash.Sha256FileAsync(f.Path, null, ct);
                if (sha != f.Sha256)
                {
                    return Loc.T("eng.came_back_different", f.Path);
                }
                f.Residency = Residency.Local;
                f.EvaporatedUtc = null;
                Ledger.Upsert(f);
                Ledger.Log("j.brought_back", f.Path, f.Size, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CloudFilesException)
            {
                Ledger.Log("j.bring_back_failed", f.Path, f.Size, false, e.Message);
                return $"{f.Path}: {e.Message}";
            }
        }
        return null;
    }

    /// <summary>Copies files (typically unique ones: your own outputs, LoRA, datasets) into the mirror
    /// store, verified, so they gain an independent source. Returns how many were replicated.</summary>
    public async Task<int> ReplicateAsync(IEnumerable<FileRecord> files, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        if (Settings.MirrorRoot is not { Length: > 0 } root || !Directory.Exists(root))
        {
            throw new InvalidOperationException(Loc.T("eng.no_mirror"));
        }
        var store = new MirrorStore(root);
        var n = 0;
        foreach (var f in files.Where(f => f.Residency == Residency.Local))
        {
            ct.ThrowIfCancellationRequested();
            var p = new Progress<long>(b => progress?.Report(new ScanProgress(Loc.T("phase.replicate"), f.Path, b, f.Size)));
            try
            {
                var source = await store.ReplicateAsync(f.Path, f.Sha256, p, ct);
                Ledger.PutSource(source);
                (f.Class, f.SourceLabel) = new SourceResolver(Ledger, Settings, _hub).Classify(f);
                Ledger.Upsert(f);
                Ledger.Log("j.replicated", f.Path, f.Size, true, source.Locator);
                n++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Ledger.Log("j.replicate_failed", f.Path, f.Size, false, e.Message);
            }
        }
        return n;
    }

    /// <summary>Reverts a placeholder, waiting (up to 60 s) for Marea's own post-hydration check
    /// or another short-lived reader to release the file.</summary>
    private static async Task RevertWhenFreeAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Placeholder.Revert(path);
                return;
            }
            catch (IOException) when (attempt < 120)
            {
                await Task.Delay(500, ct);
            }
        }
    }

    /// <summary>Emergency exit: brings everything back, turns placeholders into ordinary files and
    /// unregisters every folder. After this Windows has no trace of Marea.</summary>
    public async Task<string?> RestoreEverythingAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var problem = await BringBackAsync(Ledger.Files(), progress, ct);
        if (problem is not null)
        {
            return problem;
        }
        foreach (var root in Settings.ManagedRoots.ToList())
        {
            if (TryRelease(root) is { } stuck)
            {
                return stuck;
            }
        }
        Ledger.Log("j.restore_all", "", 0, true);
        return null;
    }

    /// <summary>ReleaseRoot that reports instead of throwing.</summary>
    private string? TryRelease(string root)
    {
        try
        {
            ReleaseRoot(root);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or CloudFilesException)
        {
            Ledger.Log("j.bring_back_failed", root, 0, false, ex.Message);
            return ex.Message;
        }
    }

    /// <summary>Detaches a folder from Windows. Windows deletes every placeholder that is not hydrated
    /// when a sync root is unregistered, so this refuses unless the whole folder holds only ordinary files.</summary>
    /// <exception cref="InvalidOperationException">When any placeholder is still present.</exception>
    private void ReleaseRoot(string root)
    {
        if (!SyncRoot.IsMareaRoot(root))
        {
            return;
        }
        var leftovers = PlaceholdersUnder(root).Take(3).ToList();
        if (leftovers.Count > 0)
        {
            throw new InvalidOperationException(Loc.T("eng.placeholders_left", root, string.Join("\n", leftovers)));
        }
        foreach (var c in _connections.Where(c => string.Equals(c.Root, root, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            c.Dispose();
            _connections.Remove(c);
        }
        SyncRoot.Unregister(root);
    }

    /// <summary>Every file under <paramref name="root"/> that is still a placeholder (any size, tracked or not).</summary>
    public static IEnumerable<string> PlaceholdersUnder(string root)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var path in Directory.EnumerateFiles(root, "*", opts))
        {
            if (Placeholder.GetState(path) != PlaceholderState.NormalFile)
            {
                yield return path;
            }
        }
    }

    /// <summary>Null when the volume can take <paramref name="bytes"/> more (keeping 1 GiB spare), else why not.</summary>
    private static string? SpaceProblem(IEnumerable<FileRecord> coming)
    {
        foreach (var byVolume in coming.GroupBy(f => FileSystemFacts.VolumeRoot(f.Path), StringComparer.OrdinalIgnoreCase))
        {
            var need = byVolume.Sum(f => f.Size);
            var free = FileSystemFacts.FreeBytes(byVolume.Key);
            if (need > free - (1L << 30))
            {
                return Loc.T("eng.no_space", byVolume.Key, Math.Round(need / (double)(1L << 30), 1), Math.Round(free / (double)(1L << 30), 1));
            }
        }
        return null;
    }

    /// <summary>Tracked files grouped into models, with what their headers say (format, precision, safety).</summary>
    public List<ModelEntry> Models() =>
        ModelCatalog.Build(Ledger.Files(), Ledger.AllMeta(), hubScans: Ledger.HubScans()).Where(m => m.IsModel).ToList();

    /// <summary>Space status of every volume with managed folders.</summary>
    public List<VolumeStatus> Volumes() => Settings.ManagedRoots
        .Where(Directory.Exists)
        .Select(FileSystemFacts.VolumeRoot)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(v => new VolumeStatus(v, FileSystemFacts.FreeBytes(v), FileSystemFacts.TotalBytes(v),
            (long)(Settings.FreeSpaceTargetGb * (1L << 30))))
        .ToList();

    /// <summary>One tide cycle: when a volume is below target, evaporates the cheapest files. Returns bytes freed.</summary>
    public async Task<long> RunTideAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        long freed = 0;
        foreach (var v in Volumes().Where(v => v.Deficit > 0))
        {
            var plan = Plan(v.Deficit).Where(p => string.Equals(FileSystemFacts.VolumeRoot(p.File.Path), v.Volume, StringComparison.OrdinalIgnoreCase));
            var files = plan.Select(p => p.File).ToList();
            var (done, _) = await EvaporateAsync(files, progress, ct);
            freed += files.Take(done).Sum(f => f.Size);
            Ledger.Log("j.tide", v.Volume, freed, true, Loc.Code("eng.tide_detail", done));
        }
        return freed;
    }

    /// <summary>Becomes the provider for this user session (one process only). Returns false when
    /// another Marea process already serves the files.</summary>
    public bool StartProvider()
    {
        if (IsProvider)
        {
            return true;
        }
        try
        {
            // An exclusive lock file: Windows releases it automatically if the process dies.
            _providerLock = new FileStream(Path.Combine(DataDir, "provider.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return false;
        }
        IsProvider = true;
        foreach (var root in Settings.ManagedRoots.Where(r => Directory.Exists(r) && SyncRoot.IsMareaRoot(r)))
        {
            EnsureConnected(root);
        }
        return true;
    }

    private FileStream? _providerLock;

    /// <summary>True when this process serves hydration requests.</summary>
    public bool IsProvider { get; private set; }

    private void EnsureConnected(string root)
    {
        if (!IsProvider || _connections.Any(c => string.Equals(c.Root, root, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        _connections.Add(ProviderConnection.Connect(root, req => Hydration.HandleAsync(req, root)));
    }

    /// <summary>Disconnects and releases the provider role.</summary>
    public void Dispose()
    {
        foreach (var c in _connections)
        {
            c.Dispose();
        }
        _connections.Clear();
        _providerLock?.Dispose();
        IsProvider = false;
        _hub.Dispose();
        _ollama.Dispose();
    }
}
