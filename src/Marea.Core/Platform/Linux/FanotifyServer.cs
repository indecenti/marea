using System.Collections.Concurrent;
using Marea.Core;

namespace Marea.CloudFiles;

/// <summary>The Linux agent's core: one fanotify group with a FAN_OPEN_PERM mark on every freed file (inode
/// marks, so ordinary files cost nothing). When a program opens a freed file the kernel holds the open, the
/// agent refills the file from its sources, checks the sha256, and only then lets the open through. If the
/// bytes cannot be delivered or verified the open fails with a permission error: a program never sees a
/// half or wrong file. Needs CAP_SYS_ADMIN (the systemd service grants it).</summary>
public sealed class FanotifyServer : IDisposable
{
    private const uint ClassContent = 0x04, CloExec = 0x01, NonBlock = 0x02, UnlimitedQueue = 0x10, UnlimitedMarks = 0x20;
    private const uint MarkAdd = 0x01, MarkRemove = 0x02, MarkDontFollow = 0x04;
    private const ulong OpenPerm = 0x10000;
    private const uint Allow = 1, Deny = 2;
    private const uint EventFlags = 0x02 /* O_RDWR */ | 0x80000 /* O_CLOEXEC */;

    private readonly int _fd;
    private readonly Func<FetchRequest, Task> _handler;
    private readonly ConcurrentDictionary<(ulong, ulong), SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<FetchRequest, byte> _inflight = new();
    private readonly Thread _reader;
    private readonly AgentSocket _socket;
    private volatile bool _stopping;

    /// <summary>The running server of this process, or null (every process but the agent).</summary>
    public static FanotifyServer? Current { get; private set; }

    private FanotifyServer(int fd, Func<FetchRequest, Task> handler)
    {
        _fd = fd;
        _handler = handler;
        _socket = AgentSocket.Listen(AgentLink.SocketPath, Answer);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "marea-fanotify" };
        _reader.Start();
    }

    /// <summary>Makes this process the agent. Returns null on success or why it cannot serve.</summary>
    public static string? Start(Func<FetchRequest, Task> handler)
    {
        if (Current is not null)
        {
            return null;
        }
        var fd = LinuxNative.fanotify_init(ClassContent | CloExec | NonBlock | UnlimitedQueue | UnlimitedMarks, EventFlags);
        if (fd < 0)
        {
            var errno = LinuxNative.Errno;
            return errno == LinuxNative.EPerm ? Loc.T("linux.no_capability") : Loc.T("linux.fanotify_failed", LinuxNative.ErrText(errno));
        }
        Current = new FanotifyServer(fd, handler);
        return null;
    }

    /// <summary>Watches one freed file. Only regular files of the agent's own user that carry the identity
    /// attribute qualify. Returns the file's (device, inode) so the caller can check it marked the right one.</summary>
    /// <exception cref="CloudFilesException">When the file does not qualify or the kernel refuses.</exception>
    public (ulong, ulong) Arm(string path, uint requester)
    {
        var st = LinuxNative.LStat(path);
        if (!st.IsRegular || st.Uid != LinuxNative.geteuid() || (requester != 0 && requester != st.Uid)
            || !LinuxNative.HasXattr(path, Placeholder.IdAttr))
        {
            throw new CloudFilesException(Loc.T("ev.unexpected_state", path), 0);
        }
        if (LinuxNative.fanotify_mark(_fd, MarkAdd | MarkDontFollow, OpenPerm, LinuxNative.AtFdCwd, path) != 0)
        {
            throw new CloudFilesException($"fanotify_mark: {LinuxNative.ErrText(LinuxNative.Errno)}", LinuxNative.Errno);
        }
        return (st.Dev, st.Ino);
    }

    /// <summary>Arms every freed file under <paramref name="root"/> (after a restart). Returns how many.</summary>
    public int ArmUnder(string root)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var n = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", opts))
        {
            if (LinuxNative.HasXattr(path, Placeholder.IdAttr))
            {
                try
                {
                    Arm(path, 0);
                    n++;
                }
                catch (Exception e) when (e is CloudFilesException or IOException)
                {
                    Console.Error.WriteLine($"marea: {path}: {e.Message}");
                }
            }
        }
        return n;
    }

    /// <summary>Requests from mareactl over the agent socket: "PING" and "ARM\t&lt;base64 path&gt;".</summary>
    private string Answer(string line, uint peer)
    {
        var parts = line.Split('\t');
        try
        {
            return parts[0] switch
            {
                "PING" => "OK\t1",
                "ARM" when parts.Length == 2 => Armed(Arm(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])), peer)),
                _ => "ERR\tunknown request",
            };
        }
        catch (Exception e) when (e is CloudFilesException or IOException or FormatException)
        {
            return "ERR\t" + e.Message.ReplaceLineEndings(" ");
        }
        static string Armed((ulong Dev, ulong Ino) id) => $"OK\t{id.Dev}\t{id.Ino}";
    }

    private unsafe void ReadLoop()
    {
        var buf = new byte[64 * 1024];
        var pfd = stackalloc int[2];
        while (!_stopping)
        {
            pfd[0] = _fd;
            pfd[1] = 1; // POLLIN
            if (LinuxNative.poll(pfd, 1, 500) <= 0)
            {
                continue;
            }
            nint n;
            fixed (byte* p = buf)
            {
                n = LinuxNative.read(_fd, p, buf.Length);
            }
            if (n <= 0)
            {
                continue;
            }
            for (var off = 0; off + 24 <= n;)
            {
                var len = BitConverter.ToInt32(buf, off);
                var mask = BitConverter.ToUInt64(buf, off + 8);
                var efd = BitConverter.ToInt32(buf, off + 16);
                var pid = BitConverter.ToInt32(buf, off + 20);
                if (buf[off + 4] != 3 || len < 24)
                {
                    return; // unknown metadata version: never guess at the layout
                }
                if (efd >= 0 && (mask & OpenPerm) != 0)
                {
                    _ = Task.Run(() => ServeAsync(efd, pid));
                }
                else if (efd >= 0)
                {
                    LinuxNative.close(efd);
                }
                off += len;
            }
        }
    }

    /// <summary>Answers one open: allow at once for a file that is no longer freed, otherwise refill and verify.</summary>
    private async Task ServeAsync(int efd, int pid)
    {
        var allow = false;
        try
        {
            if (LinuxNative.GetXattr(efd, Placeholder.IdAttr) is null)
            {
                allow = true;
                return;
            }
            var st = LinuxNative.Stat(efd);
            var gate = _gates.GetOrAdd((st.Dev, st.Ino), _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                allow = await RefillAsync(efd, pid, st).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"marea: refill failed: {e.Message}");
        }
        finally
        {
            Respond(efd, allow);
        }
    }

    private async Task<bool> RefillAsync(int efd, int pid, LinuxStat st)
    {
        if (LinuxNative.GetXattr(efd, Placeholder.IdAttr) is not { } id)
        {
            return true; // another open brought it back while this one waited
        }
        var image = pid > 0 ? LinuxNative.ReadLink($"/proc/{pid}/exe") : null;
        var req = new FetchRequest(efd, id, LinuxNative.FdPath(efd), st.Size, (uint)Math.Max(pid, 0), image);
        _inflight[req] = 0;
        try
        {
            await _handler(req).ConfigureAwait(false);
        }
        catch (Exception)
        {
            req.Fail(0, st.Size);
        }
        finally
        {
            _inflight.TryRemove(req, out _);
        }
        if (!_stopping && req.Complete())
        {
            LinuxNative.RemoveXattr(efd, Placeholder.IdAttr);
            LinuxNative.RestoreWriteTime(efd, st);
            _ = LinuxNative.fanotify_mark(_fd, MarkRemove, OpenPerm, efd, null);
            return true;
        }
        Placeholder.Restub(efd, st);
        return false;
    }

    private unsafe void Respond(int efd, bool allow)
    {
        var response = stackalloc uint[2] { (uint)efd, allow ? Allow : Deny };
        _ = LinuxNative.write(_fd, (byte*)response, 8);
        LinuxNative.close(efd);
    }

    /// <summary>Stops serving: refills in progress are cancelled and their opens denied, then the group closes.</summary>
    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }
        _stopping = true;
        _socket.Dispose();
        foreach (var req in _inflight.Keys)
        {
            req._cts.Cancel();
        }
        for (var i = 0; i < 100 && !_inflight.IsEmpty; i++)
        {
            Thread.Sleep(100);
        }
        _reader.Join(2000);
        LinuxNative.close(_fd);
        Current = null;
    }
}
