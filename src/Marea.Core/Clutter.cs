namespace Marea.Core;

/// <summary>What kind of regenerable space an entry is.</summary>
public enum ClutterKind
{
    /// <summary>Download or build cache of a package manager: the tool fetches it again when needed.</summary>
    PackageCache,
    /// <summary>Entries of the temporary folder nothing has written to for a while.</summary>
    StaleTemp,
    /// <summary>Crash dumps the system keeps for debugging.</summary>
    CrashDumps,
    /// <summary>The hibernation file: space reserved by the system, removable only by turning hibernation off.</summary>
    Hibernation,
    /// <summary>A virtual machine disk: holds real data, can only be compacted.</summary>
    VirtualDisk,
}

/// <summary>One sub-entry of a clutter item (a stale temporary folder, one virtual disk).</summary>
/// <param name="Path">Full path.</param>
/// <param name="Bytes">Size on disk of the files inside.</param>
/// <param name="NewestUtc">Most recent write inside, or null when unknown.</param>
public sealed record ClutterPart(string Path, long Bytes, DateTime? NewestUtc);

/// <summary>Space that is not a model and that the machine can produce again.</summary>
/// <param name="Id">Stable code: pip, npm, temp, crash_dumps, hibernation, virtual_disks, ...</param>
/// <param name="Kind">Category.</param>
/// <param name="Path">Folder (or file) holding it.</param>
/// <param name="Bytes">Total size.</param>
/// <param name="Files">Number of files counted.</param>
/// <param name="NewestUtc">Most recent write inside.</param>
/// <param name="Command">The owner tool's own clean command, when it has one.</param>
/// <param name="Reclaimable">True when removing it loses nothing; false when it needs a decision.</param>
/// <param name="Parts">Sub-entries, largest first (may be empty).</param>
/// <param name="SharedBytes">Bytes of files that have another hard link elsewhere (a virtual environment,
/// a project): they stay on disk when this item is emptied.</param>
/// <param name="ToolFound">For a cache: whether the owner tool is on PATH (null when not checked).</param>
public sealed record ClutterItem(string Id, ClutterKind Kind, string Path, long Bytes, int Files, DateTime? NewestUtc,
    string? Command, bool Reclaimable, IReadOnlyList<ClutterPart> Parts, long SharedBytes = 0, bool? ToolFound = null)
{
    /// <summary>Bytes the disk really gets back when the item is emptied.</summary>
    public long FreeableBytes => Bytes - SharedBytes;
}

/// <summary>A cache folder of a known tool.</summary>
/// <param name="Id">Stable code of the tool.</param>
/// <param name="Path">Where the cache lives.</param>
/// <param name="Command">The tool's own clean command, or null when it has none.</param>
/// <param name="Tool">Executable that runs the command (looked up on PATH), or null.</param>
/// <param name="HardLinks">True when the tool hard-links cached files into projects, so a cached file
/// may not be freed by emptying the cache.</param>
public sealed record CacheSpot(string Id, string Path, string? Command, string? Tool = null, bool HardLinks = false);

/// <summary>Where to look. Tests pass their own folders; <see cref="Current"/> describes this machine.</summary>
/// <param name="Caches">Candidate cache folders (missing ones are skipped).</param>
/// <param name="Temp">Temporary folder, or null to skip it.</param>
/// <param name="CrashDumps">Crash-dump folder, or null.</param>
/// <param name="HibernationFile">Hibernation file, or null.</param>
/// <param name="VirtualDiskRoots">Folders searched (three levels deep) for virtual disk images.</param>
public sealed record ClutterPlaces(IReadOnlyList<CacheSpot> Caches, string? Temp, string? CrashDumps,
    string? HibernationFile, IReadOnlyList<string> VirtualDiskRoots)
{
    /// <summary>The standard locations for the current user, honouring each tool's override variable.</summary>
    public static ClutterPlaces Current()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var caches = KnownCaches(home, local, Environment.GetEnvironmentVariable).ToList();
        var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        if (!OperatingSystem.IsWindows())
        {
            return new ClutterPlaces(caches, temp, null, null, []);
        }
        var system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? @"C:\";
        return new ClutterPlaces(caches, temp, Path.Combine(local, "CrashDumps"),
            Path.Combine(system, "hiberfil.sys"),
            [Path.Combine(local, "wsl"), Path.Combine(local, "Packages"), Path.Combine(local, "Docker", "wsl")]);
    }

    /// <summary>Cache folders of common package managers on Windows and Linux. An override variable
    /// (PIP_CACHE_DIR, UV_CACHE_DIR, ...) replaces the default locations of its tool.</summary>
    public static IEnumerable<CacheSpot> KnownCaches(string home, string local, Func<string, string?> env)
    {
        IEnumerable<CacheSpot> Tool(string id, string? command, string? overrideVar, string? overrideTail, params string[] defaults)
        {
            // The command's first word is the program to look for; uv and pnpm hard-link out of their store.
            var exe = command?.Split(' ')[0];
            var links = id is "uv" or "pnpm";
            if (overrideVar is not null && env(overrideVar) is { Length: > 0 } custom)
            {
                yield return new CacheSpot(id, overrideTail is null ? custom : Path.Combine(custom, overrideTail), command, exe, links);
                yield break;
            }
            foreach (var d in defaults)
            {
                yield return new CacheSpot(id, d, command, exe, links);
            }
        }
        var cache = env("XDG_CACHE_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".cache");
        return
        [
            .. Tool("pip", "pip cache purge", "PIP_CACHE_DIR", null, Path.Combine(local, "pip", "Cache"), Path.Combine(cache, "pip")),
            .. Tool("uv", "uv cache clean", "UV_CACHE_DIR", null, Path.Combine(local, "uv", "cache"), Path.Combine(cache, "uv")),
            .. Tool("npm", "npm cache clean --force", "npm_config_cache", null, Path.Combine(local, "npm-cache"), Path.Combine(home, ".npm", "_cacache")),
            .. Tool("yarn", "yarn cache clean", "YARN_CACHE_FOLDER", null, Path.Combine(local, "Yarn", "Cache"), Path.Combine(cache, "yarn")),
            .. Tool("pnpm", "pnpm store prune", null, null, Path.Combine(local, "pnpm", "store"), Path.Combine(home, ".local", "share", "pnpm", "store")),
            .. Tool("nuget", "dotnet nuget locals all --clear", "NUGET_PACKAGES", null, Path.Combine(home, ".nuget", "packages")),
            .. Tool("go", "go clean -cache", "GOCACHE", null, Path.Combine(local, "go-build"), Path.Combine(cache, "go-build")),
            .. Tool("gradle", null, "GRADLE_USER_HOME", "caches", Path.Combine(home, ".gradle", "caches")),
            .. Tool("cargo", null, "CARGO_HOME", "registry", Path.Combine(home, ".cargo", "registry")),
        ];
    }
}

/// <summary>Finds space that is not a model but can be produced again: package caches, idle temporary
/// files, crash dumps; and space that needs a decision (hibernation file, virtual disks).
/// Read-only: it lists directories, sizes and write times, never reads a file's content (in a store
/// that hard-links, a file is opened only to count its links), never follows a junction or symbolic
/// link, and never deletes. Each item names the command of the tool that owns it.</summary>
public static class ClutterFinder
{
    private static readonly EnumerationOptions Deep = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly EnumerationOptions Flat = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>How deep below the temporary folder a busy, large folder is split into its entries.</summary>
    private const int TempSplitDepth = 2;

    /// <summary>Lists clutter, largest first.</summary>
    /// <param name="places">Where to look.</param>
    /// <param name="staleDays">A temporary entry counts when nothing inside was written for this many days.</param>
    /// <param name="minBytes">Items smaller than this are left out.</param>
    /// <param name="nowUtc">Clock, for tests.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="toolOnPath">Whether a program can be run by name; defaults to a PATH lookup.</param>
    public static List<ClutterItem> Find(ClutterPlaces places, int staleDays = 7, long minBytes = 100L << 20,
        DateTime? nowUtc = null, CancellationToken ct = default, Func<string, bool>? toolOnPath = null)
    {
        toolOnPath ??= OnPath;
        var items = new List<ClutterItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spot in places.Caches)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(spot.Path) && seen.Add(Path.GetFullPath(spot.Path)))
            {
                var s = Measure(new DirectoryInfo(spot.Path), ct, spot.HardLinks);
                items.Add(new ClutterItem(spot.Id, ClutterKind.PackageCache, spot.Path, s.Bytes, s.Files, s.Newest, spot.Command, true, [],
                    s.Shared, spot.Tool is null ? null : toolOnPath(spot.Tool)));
            }
        }
        if (places.Temp is { } temp && Directory.Exists(temp))
        {
            var cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-Math.Max(0, staleDays));
            var parts = new List<ClutterPart>();
            var files = 0;
            foreach (var e in Children(new DirectoryInfo(temp)))
            {
                files += Stale(e, 1, cutoff, minBytes, parts, ct);
            }
            parts.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            items.Add(new ClutterItem("temp", ClutterKind.StaleTemp, temp, parts.Sum(p => p.Bytes), files,
                parts.Count == 0 ? null : parts.Max(p => p.NewestUtc), null, true, parts));
        }
        if (places.CrashDumps is { } dumps && Directory.Exists(dumps))
        {
            var s = Measure(new DirectoryInfo(dumps), ct);
            items.Add(new ClutterItem("crash_dumps", ClutterKind.CrashDumps, dumps, s.Bytes, s.Files, s.Newest, null, true, []));
        }
        if (places.HibernationFile is { } hib && FileSize(hib) is long hibBytes)
        {
            items.Add(new ClutterItem("hibernation", ClutterKind.Hibernation, hib, hibBytes, 1, null, "powercfg /h off", false, []));
        }
        var disks = places.VirtualDiskRoots.Where(Directory.Exists).SelectMany(r => VirtualDisks(r, ct))
            .OrderByDescending(p => p.Bytes).ToList();
        if (disks.Count > 0)
        {
            items.Add(new ClutterItem("virtual_disks", ClutterKind.VirtualDisk, CommonParent(disks.Select(d => d.Path)),
                disks.Sum(d => d.Bytes), disks.Count, disks.Max(d => d.NewestUtc), null, false, disks));
        }
        return items.Where(i => i.Bytes >= minBytes).OrderByDescending(i => i.Reclaimable).ThenByDescending(i => i.FreeableBytes).ToList();
    }

    /// <summary>Adds <paramref name="e"/> to <paramref name="parts"/> when nothing inside is newer than the
    /// cutoff. A recent but large folder is judged entry by entry, down to <see cref="TempSplitDepth"/>:
    /// a busy parent often holds many abandoned work folders. Returns the files counted.</summary>
    private static int Stale(FileSystemInfo e, int depth, DateTime cutoff, long minBytes, List<ClutterPart> parts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var s = e is DirectoryInfo d ? Measure(d, ct) : new Stat(Length(e), 1, WriteTime(e));
        if (s.Files == 0 || s.Newest is null)
        {
            return 0;
        }
        if (s.Newest < cutoff)
        {
            parts.Add(new ClutterPart(e.FullName, s.Bytes, s.Newest));
            return s.Files;
        }
        if (e is not DirectoryInfo dir || depth >= TempSplitDepth || s.Bytes < minBytes)
        {
            return 0;
        }
        var n = 0;
        foreach (var child in Children(dir).OfType<DirectoryInfo>())
        {
            n += Stale(child, depth + 1, cutoff, minBytes, parts, ct);
        }
        return n;
    }

    private readonly record struct Stat(long Bytes, int Files, DateTime? Newest, long Shared = 0);

    /// <summary>Files smaller than this are not worth an open just to count their links: a store is
    /// mostly large binaries by size, and opening every small file would make the walk many times slower.</summary>
    private const long LinkCheckMinBytes = 1 << 20;

    private static Stat Measure(DirectoryInfo d, CancellationToken ct, bool countLinks = false)
    {
        long bytes = 0;
        long shared = 0;
        var n = 0;
        DateTime? newest = null;
        try
        {
            foreach (var f in d.EnumerateFiles("*", Deep))
            {
                if ((n & 0xFFF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }
                var len = Length(f);
                bytes += len;
                n++;
                if (countLinks && len >= LinkCheckMinBytes && Links(f.FullName) > 1)
                {
                    shared += len;
                }
                if (WriteTime(f) is { } t && (newest is null || t > newest))
                {
                    newest = t;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A folder that vanishes or closes while being listed: report what was counted.
        }
        return new Stat(bytes, n, newest, shared);
    }

    /// <summary>Translation key suffix of a kind (clutter.kind.package_cache, ...).</summary>
    public static string KindKey(ClutterKind k) => k switch
    {
        ClutterKind.PackageCache => "package_cache",
        ClutterKind.StaleTemp => "stale_temp",
        ClutterKind.CrashDumps => "crash_dumps",
        ClutterKind.Hibernation => "hibernation",
        _ => "virtual_disk",
    };

    /// <summary>What to do about an item, in the active language: first the owner tool's command or the
    /// rule that makes it safe, then what the figure leaves out (hard-linked bytes, a tool not on PATH).</summary>
    public static IReadOnlyList<string> Advice(ClutterItem i, int staleDays)
    {
        var lines = new List<string>
        {
            i.Kind switch
            {
                ClutterKind.PackageCache => i.Command is { } c
                    ? Loc.T("clutter.hint.command", i.Id, c)
                    : Loc.T("clutter.hint.no_command", i.Id),
                ClutterKind.StaleTemp => Loc.T("clutter.hint.temp", i.Parts.Count, staleDays),
                ClutterKind.CrashDumps => Loc.T("clutter.hint.crash_dumps"),
                ClutterKind.Hibernation => Loc.T("clutter.hint.hibernation", i.Command ?? ""),
                _ => Loc.T("clutter.hint.virtual_disk", i.Files),
            },
        };
        if (i.SharedBytes > 0)
        {
            lines.Add(Loc.T("clutter.hint.shared", $"{i.SharedBytes / (double)(1L << 30):0.0} GB"));
        }
        if (i.ToolFound == false)
        {
            lines.Add(Loc.T("clutter.hint.tool_missing", i.Id));
        }
        return lines;
    }

    /// <summary>Hard links to a file; 1 when it cannot be asked (in use, no permission).</summary>
    private static int Links(string path)
    {
        try
        {
            using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FileSystemFacts.HardLinkCount(h);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    /// <summary>True when <paramref name="program"/> can be started by name from PATH.</summary>
    public static bool OnPath(string program)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string[] exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                try
                {
                    if (File.Exists(Path.Combine(dir.Trim('"'), program + ext)))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry: skip it.
                }
            }
        }
        return false;
    }

    private static List<FileSystemInfo> Children(DirectoryInfo d)
    {
        try
        {
            return d.EnumerateFileSystemInfos("*", Flat).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<ClutterPart> VirtualDisks(string root, CancellationToken ct)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        List<FileInfo> found;
        try
        {
            found = new DirectoryInfo(root).EnumerateFiles("*.vhdx", opts).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var f in found)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ClutterPart(f.FullName, Length(f), WriteTime(f));
        }
    }

    private static long Length(FileSystemInfo f)
    {
        try
        {
            return f is FileInfo fi ? fi.Length : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static DateTime? WriteTime(FileSystemInfo f)
    {
        try
        {
            return f.LastWriteTimeUtc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static long? FileSize(string path)
    {
        try
        {
            var f = new FileInfo(path);
            return f.Exists ? f.Length : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CommonParent(IEnumerable<string> paths)
    {
        var dirs = paths.Select(p => Path.GetDirectoryName(p) ?? p).ToList();
        var common = dirs[0];
        foreach (var d in dirs.Skip(1))
        {
            while (common.Length > 0 && !string.Equals(d, common, StringComparison.OrdinalIgnoreCase) && !FileSystemFacts.IsUnder(d, common))
            {
                common = Path.GetDirectoryName(common) ?? "";
            }
        }
        return common;
    }
}
