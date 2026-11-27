using System.Text;
using Marea.Core;
using Microsoft.Win32.SafeHandles;

namespace Marea.CloudFiles;

/// <summary>Observable on-disk state of a managed file (same names as on Windows).</summary>
public enum PlaceholderState
{
    /// <summary>Ordinary file with all its data.</summary>
    NormalFile,
    /// <summary>Not used on Linux: a file that came back is an ordinary file again.</summary>
    Hydrated,
    /// <summary>Freed: same size, no data on disk, comes back when a program opens it.</summary>
    Dehydrated,
    /// <summary>Not used on Linux.</summary>
    Modified,
}

/// <summary>Linux version of the per-file operations. A freed file keeps its name, size and write time;
/// its blocks are released (a sparse file), its sha256 identity sits in the <c>user.marea.id</c> extended
/// attribute and its first bytes say what it is. The Marea agent watches it with fanotify and refills it,
/// verified, before the program that opens it can read a byte.</summary>
public static class Placeholder
{
    internal const string IdAttr = "user.marea.id";

    /// <summary>Frees the file behind an exclusive handle. Refuses (IOException) when any other program has it
    /// open or mapped: a running model server would otherwise read zeros.</summary>
    /// <exception cref="IOException">The file is in use.</exception>
    /// <exception cref="CloudFilesException">The agent is not running or the file system cannot do it.</exception>
    public static void ConvertAndDehydrate(SafeFileHandle exclusiveHandle, ReadOnlySpan<byte> identity)
    {
        if (identity.Length is 0 or > 4096)
        {
            throw new ArgumentException("identity must be 1-4096 bytes", nameof(identity));
        }
        var fd = (int)exclusiveHandle.DangerousGetHandle();
        var before = LinuxNative.Stat(fd);
        var path = LinuxNative.FdPath(fd);
        using var lease = WriteLease.Take(fd);
        // Setting an attribute needs write permission at that moment: a read-only file (huggingface_hub
        // 2.x shared blobs are 0444) gets the owner write bit for the operation and its mode back after.
        using var writable = WritableWhile.Lift(fd, before);
        LinuxNative.SetXattr(fd, IdAttr, identity);
        try
        {
            var armed = FanotifyServer.Current?.Arm(path, LinuxNative.geteuid()) ?? AgentLink.Arm(path);
            if (armed != (before.Dev, before.Ino))
            {
                throw new CloudFilesException(Loc.T("ev.unexpected_state", path), 0);
            }
            if (LinuxNative.PunchHole(fd, before.Size) != 0)
            {
                throw new CloudFilesException(Loc.T("linux.fs_unsupported"), LinuxNative.Errno);
            }
        }
        catch
        {
            // Nothing was released yet: the file is still whole, so it must not look freed.
            LinuxNative.RemoveXattr(fd, IdAttr);
            throw;
        }
        WriteNotice(fd, before);
    }

    /// <summary>Frees again a file that still carries its identity (e.g. a refill that did not verify).</summary>
    public static void Dehydrate(SafeFileHandle exclusiveHandle)
    {
        var fd = (int)exclusiveHandle.DangerousGetHandle();
        if (LinuxNative.GetXattr(fd, IdAttr) is null)
        {
            throw new CloudFilesException(Loc.T("ev.unexpected_state", PlaceholderState.NormalFile), 0);
        }
        using var lease = WriteLease.Take(fd);
        Restub(fd, LinuxNative.Stat(fd));
    }

    /// <summary>Releases the blocks of an identified file again and rewrites the notice (agent side, after a failed refill).</summary>
    internal static void Restub(int fd, LinuxStat st)
    {
        _ = LinuxNative.PunchHole(fd, st.Size);
        WriteNotice(fd, st);
    }

    /// <summary>The first bytes of a freed file explain it, so a program that reads it while the agent is
    /// stopped fails on an obviously wrong header instead of loading zeros.</summary>
    private static void WriteNotice(int fd, LinuxStat st)
    {
        var notice = Encoding.ASCII.GetBytes(
            "MAREA-FREED-FILE\nThis file's data was freed by Marea (https://github.com/indecenti/marea) and comes back " +
            "automatically when a program opens it, while the Marea agent runs. Start it: sudo mareactl service install\n");
        try
        {
            LinuxNative.WriteAt(fd, notice.AsSpan(0, (int)Math.Min(notice.Length, st.Size)), 0);
        }
        finally
        {
            LinuxNative.RestoreWriteTime(fd, st);
        }
    }

    /// <summary>Brings the whole file back now: opening it is enough, the agent refills it during the open.</summary>
    /// <exception cref="CloudFilesException">When no agent runs or the file could not come back.</exception>
    public static void Hydrate(string path)
    {
        if (GetState(path) != PlaceholderState.Dehydrated)
        {
            return;
        }
        if (FanotifyServer.Current is null && !AgentLink.Ping())
        {
            throw new CloudFilesException(Loc.T("linux.agent_needed"), 0);
        }
        try
        {
            using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (UnauthorizedAccessException)
        {
            // The agent denied the open: no live source delivered the right bytes.
        }
        if (GetState(path) == PlaceholderState.Dehydrated)
        {
            throw new CloudFilesException(Loc.T("linux.not_back", path), 0);
        }
    }

    /// <summary>On Linux a file that came back is already ordinary; waits (IOException) while it is still freed.</summary>
    public static void Revert(string path)
    {
        if (GetState(path) == PlaceholderState.Dehydrated)
        {
            throw new IOException(Loc.T("linux.not_back", path));
        }
    }

    /// <summary>Reads the state from the extended attribute only: never opens the file (an open would refill it).</summary>
    public static PlaceholderState GetState(string path) =>
        LinuxNative.HasXattr(path, IdAttr) ? PlaceholderState.Dehydrated : PlaceholderState.NormalFile;

    /// <summary>Bytes the file really occupies on disk.</summary>
    public static long SizeOnDisk(string path) => LinuxNative.LStat(path).Blocks * 512;
}

/// <summary>Gives a read-only file its owner write bit until disposed, then restores the exact mode.</summary>
internal sealed class WritableWhile : IDisposable
{
    private readonly int _fd;
    private readonly uint _mode;

    private WritableWhile(int fd, uint mode)
    {
        _fd = fd;
        _mode = mode;
    }

    /// <summary>Lifts the bit when missing; returns null when the file is already writable by its owner.</summary>
    public static WritableWhile? Lift(int fd, LinuxStat st)
    {
        var mode = (uint)st.Mode & 0xFFF;
        if ((mode & 0x80) != 0)
        {
            return null;
        }
        if (LinuxNative.fchmod(fd, mode | 0x80) != 0)
        {
            throw new UnauthorizedAccessException($"chmod: {LinuxNative.ErrText(LinuxNative.Errno)}");
        }
        return new WritableWhile(fd, mode);
    }

    /// <summary>Puts the original mode back.</summary>
    public void Dispose() => LinuxNative.fchmod(_fd, _mode);
}

/// <summary>A kernel write lease: granted only when no other descriptor or memory mapping of the file exists,
/// and while held, other programs' opens wait. It is Linux's equivalent of Windows' exclusive share mode.</summary>
internal sealed class WriteLease : IDisposable
{
    private const int SetLease = 1024, WriteLock = 1, Unlock = 2;
    private readonly int _fd;

    private WriteLease(int fd) => _fd = fd;

    static WriteLease() => LinuxNative.IgnoreSigio();

    /// <summary>Takes the lease or throws IOException when the file is in use.</summary>
    public static WriteLease Take(int fd)
    {
        if (LinuxNative.fcntl(fd, SetLease, WriteLock) == 0)
        {
            return new WriteLease(fd);
        }
        var errno = LinuxNative.Errno;
        throw errno is LinuxNative.EAgain or 16 /* EBUSY */
            ? new IOException(Loc.T("ev.in_use"))
            : new CloudFilesException(Loc.T("linux.fs_unsupported") + $" (lease: {LinuxNative.ErrText(errno)})", errno);
    }

    /// <summary>Releases the lease: waiting opens continue (and the agent refills the file for them).</summary>
    public void Dispose() => LinuxNative.fcntl(_fd, SetLease, Unlock);
}
