namespace Marea.Core;

/// <summary>A program that brought many evaporated files back in a short time.</summary>
/// <param name="Process">Executable path as Windows reports it (NT form).</param>
/// <param name="Files">Distinct files it brought back inside the window.</param>
/// <param name="Bytes">Their total size.</param>
public sealed record MassRead(string Process, int Files, long Bytes)
{
    /// <summary>Executable name for display (e.g. backup.exe).</summary>
    public string Name => ReaderGuard.DisplayName(Process);
}

/// <summary>Watches which programs bring evaporated files back. Antivirus scans, indexers, backup
/// tools or other model managers can read every file at once and silently undo a cleanup: the guard
/// notices it, and refuses reads from programs the user chose to block.</summary>
public sealed class ReaderGuard(MareaSettings settings, Func<DateTime>? clock = null)
{
    /// <summary>Distinct files within <see cref="Window"/> that count as a mass read.</summary>
    public const int FileThreshold = 10;
    /// <summary>Bytes within <see cref="Window"/> that count as a mass read.</summary>
    public const long ByteThreshold = 50L << 30;
    /// <summary>Sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly Dictionary<string, List<(DateTime At, string Path, long Size)>> _reads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _alerted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>True when the user blocked this program from bringing files back.</summary>
    public bool IsBlocked(string? process) =>
        process is { Length: > 0 } && settings.BlockedReaders.Contains(process, StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that <paramref name="process"/> started bringing <paramref name="path"/> back;
    /// returns an alert the first time the program crosses a threshold within the window.</summary>
    public MassRead? Record(string? process, string path, long size)
    {
        if (process is not { Length: > 0 } || IsMarea(process)
            || settings.AllowedReaders.Contains(process, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }
        var now = _now();
        lock (_lock)
        {
            if (!_reads.TryGetValue(process, out var list))
            {
                _reads[process] = list = [];
            }
            list.RemoveAll(r => now - r.At > Window);
            if (!list.Exists(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add((now, path, size));
            }
            var bytes = list.Sum(r => r.Size);
            var crossed = list.Count >= FileThreshold || bytes >= ByteThreshold;
            if (!crossed || (_alerted.TryGetValue(process, out var last) && now - last < Window))
            {
                return null;
            }
            _alerted[process] = now;
            return new MassRead(process, list.Count, bytes);
        }
    }

    /// <summary>File name of an executable path in NT or Win32 form.</summary>
    public static string DisplayName(string process)
    {
        var i = process.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? process[(i + 1)..] : process;
    }

    /// <summary>Marea's own reads (bring back, undo, restore) never count.</summary>
    private static bool IsMarea(string process) =>
        DisplayName(process) is var n && (n.Equals("Marea.exe", StringComparison.OrdinalIgnoreCase)
                                          || n.Equals("mareactl.exe", StringComparison.OrdinalIgnoreCase)
                                          || n.Equals("mareactl", StringComparison.Ordinal));
}
