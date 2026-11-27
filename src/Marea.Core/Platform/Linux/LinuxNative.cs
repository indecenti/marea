using System.Runtime.InteropServices;
using System.Text;

namespace Marea.CloudFiles;

/// <summary>Identity and metadata of a file as statx reports it (layout is the same on every architecture).</summary>
internal readonly record struct LinuxStat(ulong Dev, ulong Ino, long Size, long Blocks, uint Links, uint Uid,
    ushort Mode, long MTimeSec, uint MTimeNsec)
{
    /// <summary>True for a regular file (not a link, folder or device).</summary>
    public bool IsRegular => (Mode & 0xF000) == 0x8000;
}

/// <summary>The few Linux system calls Marea needs, through glibc. Callers turn errno into exceptions.</summary>
internal static unsafe class LinuxNative
{
    private const string Libc = "libc";

    public const int AtFdCwd = -100;
    public const int EIntr = 4, EAgain = 11, EAcces = 13, ENoData = 61, ENotSup = 95, EPerm = 1;

    /// <summary>errno of the last call made through this class.</summary>
    public static int Errno => Marshal.GetLastPInvokeError();

    /// <summary>Readable text for an errno value.</summary>
    public static string ErrText(int errno) => Marshal.GetPInvokeErrorMessage(errno);

    // ---- statx: identity, size, allocated blocks, owner, write time ----

    [DllImport(Libc, SetLastError = true)]
    private static extern int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte* buf);

    /// <summary>statx of an open descriptor.</summary>
    public static LinuxStat Stat(int fd) => StatCore(fd, "", 0x1000 /* AT_EMPTY_PATH */);

    /// <summary>statx of a path, not following a final symbolic link.</summary>
    public static LinuxStat LStat(string path) => StatCore(AtFdCwd, path, 0x100 /* AT_SYMLINK_NOFOLLOW */);

    private static LinuxStat StatCore(int dirfd, string path, int flags)
    {
        var b = stackalloc byte[256];
        if (statx(dirfd, path, flags, 0x7ff /* STATX_BASIC_STATS */, b) != 0)
        {
            throw new IOException($"statx: {ErrText(Errno)}");
        }
        T Read<T>(int offset) where T : unmanaged => *(T*)(b + offset);
        var dev = ((ulong)Read<uint>(136) << 32) | Read<uint>(140);
        return new LinuxStat(dev, Read<ulong>(32), (long)Read<ulong>(40), (long)Read<ulong>(48), Read<uint>(16),
            Read<uint>(20), Read<ushort>(28), Read<long>(112), Read<uint>(120));
    }

    // ---- extended attributes ----

    [DllImport(Libc, SetLastError = true)]
    private static extern nint fgetxattr(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* value, nint size);
    [DllImport(Libc, SetLastError = true)]
    private static extern nint lgetxattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* value, nint size);
    [DllImport(Libc, SetLastError = true)]
    private static extern int fsetxattr(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* value, nint size, int flags);
    [DllImport(Libc, SetLastError = true)]
    private static extern int lsetxattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* value, nint size, int flags);
    [DllImport(Libc, SetLastError = true)]
    private static extern int fremovexattr(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Libc, SetLastError = true)]
    private static extern int lremovexattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    /// <summary>Value of an attribute of an open file, or null when it has none.</summary>
    public static byte[]? GetXattr(int fd, string name)
    {
        var buf = stackalloc byte[4096];
        var n = fgetxattr(fd, name, buf, 4096);
        return n < 0 ? null : new ReadOnlySpan<byte>(buf, (int)n).ToArray();
    }

    /// <summary>True when the path itself (not a link target) carries the attribute.</summary>
    public static bool HasXattr(string path, string name) => lgetxattr(path, name, null, 0) >= 0;

    /// <summary>Sets an attribute on an open file.</summary>
    public static void SetXattr(int fd, string name, ReadOnlySpan<byte> value)
    {
        fixed (byte* p = value)
        {
            if (fsetxattr(fd, name, p, value.Length, 0) != 0)
            {
                throw new CloudFilesException($"setxattr: {ErrText(Errno)}", Errno);
            }
        }
    }

    /// <summary>Sets an attribute on a path (a folder, for the managed-root mark).</summary>
    public static void SetXattr(string path, string name, ReadOnlySpan<byte> value)
    {
        fixed (byte* p = value)
        {
            if (lsetxattr(path, name, p, value.Length, 0) != 0)
            {
                throw new CloudFilesException($"setxattr {path}: {ErrText(Errno)}", Errno);
            }
        }
    }

    /// <summary>Removes an attribute; true when it is gone (or was never there).</summary>
    public static bool RemoveXattr(int fd, string name) => fremovexattr(fd, name) == 0 || Errno == ENoData;

    /// <summary>Removes an attribute from a path; true when it is gone (or was never there).</summary>
    public static bool RemoveXattr(string path, string name) => lremovexattr(path, name) == 0 || Errno == ENoData;

    // ---- data: hole punching, positioned writes, durability ----

    [DllImport(Libc, SetLastError = true)]
    private static extern int fallocate(int fd, int mode, long offset, long len);
    [DllImport(Libc, SetLastError = true)]
    private static extern nint pwrite(int fd, byte* buf, nint count, long offset);
    [DllImport(Libc, SetLastError = true)]
    private static extern int fdatasync(int fd);

    /// <summary>Frees the disk blocks of the whole file, keeping its size (reads give zeros).</summary>
    public static int PunchHole(int fd, long size) => fallocate(fd, 0x02 | 0x01 /* PUNCH_HOLE | KEEP_SIZE */, 0, size);

    /// <summary>Writes all of <paramref name="data"/> at <paramref name="offset"/>.</summary>
    public static void WriteAt(int fd, ReadOnlySpan<byte> data, long offset)
    {
        fixed (byte* p = data)
        {
            var done = 0;
            while (done < data.Length)
            {
                var n = pwrite(fd, p + done, data.Length - done, offset + done);
                if (n < 0 && Errno == EIntr)
                {
                    continue;
                }
                if (n <= 0)
                {
                    throw new IOException($"pwrite: {ErrText(Errno)}");
                }
                done += (int)n;
            }
        }
    }

    /// <summary>Waits until the file's data is on the disk.</summary>
    public static void Flush(int fd)
    {
        if (fdatasync(fd) != 0)
        {
            throw new IOException($"fdatasync: {ErrText(Errno)}");
        }
    }

    // ---- times, leases, descriptors ----

    [DllImport(Libc, SetLastError = true)]
    private static extern int futimens(int fd, long* times);

    /// <summary>Puts the write time back to what <paramref name="st"/> recorded (punching and refilling change it).</summary>
    public static void RestoreWriteTime(int fd, LinuxStat st)
    {
        const long utimeOmit = (1L << 30) - 2;
        var t = stackalloc long[4] { 0, utimeOmit, st.MTimeSec, st.MTimeNsec };
        _ = futimens(fd, t);
    }

    [DllImport(Libc, SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);
    [DllImport(Libc, SetLastError = true)]
    private static extern nint signal(int sig, nint handler);
    [DllImport(Libc, SetLastError = true)]
    public static extern int close(int fd);
    [DllImport(Libc, SetLastError = true)]
    public static extern int fchmod(int fd, uint mode);
    [DllImport(Libc)]
    public static extern uint geteuid();

    /// <summary>Lease breaks are signalled with SIGIO, whose default action ends the process: ignore it.</summary>
    public static void IgnoreSigio() => signal(29 /* SIGIO */, 1 /* SIG_IGN */);

    [DllImport(Libc, SetLastError = true)]
    private static extern nint readlink([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte* buf, nint size);

    /// <summary>Target of a symbolic link (or /proc magic link), or null.</summary>
    public static string? ReadLink(string path)
    {
        var buf = stackalloc byte[4096];
        var n = readlink(path, buf, 4096);
        return n <= 0 ? null : Encoding.UTF8.GetString(buf, (int)n);
    }

    /// <summary>Absolute path of an open descriptor.</summary>
    public static string FdPath(int fd) => ReadLink($"/proc/self/fd/{fd}") ?? throw new IOException("readlink /proc/self/fd");

    // ---- fanotify ----

    [DllImport(Libc, SetLastError = true)]
    public static extern int fanotify_init(uint flags, uint eventFlags);
    [DllImport(Libc, SetLastError = true)]
    public static extern int fanotify_mark(int fd, uint flags, ulong mask, int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string? path);
    [DllImport(Libc, SetLastError = true)]
    public static extern nint read(int fd, byte* buf, nint count);
    [DllImport(Libc, SetLastError = true)]
    public static extern nint write(int fd, byte* buf, nint count);
    [DllImport(Libc, SetLastError = true)]
    public static extern int poll(int* fds, nuint nfds, int timeout);
}
