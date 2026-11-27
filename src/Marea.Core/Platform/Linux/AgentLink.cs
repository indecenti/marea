using System.Net.Sockets;
using System.Text;
using Marea.Core;

namespace Marea.CloudFiles;

/// <summary>mareactl's side of the agent socket (a Unix socket in the data folder, owner-only, peers checked
/// with SO_PEERCRED). Freeing a file needs the agent to watch it first, or it could not come back.</summary>
internal static class AgentLink
{
    /// <summary>Socket path, from the same data folder mareactl uses (MAREA_DATA or the default).</summary>
    public static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("MAREA_DATA") is { Length: > 0 } d ? Path.GetFullPath(d) : MareaSettings.DefaultDirectory,
            "agent.sock");

    /// <summary>True when an agent of this user answers.</summary>
    public static bool Ping() => Ask("PING") is { } r && r.StartsWith("OK", StringComparison.Ordinal);

    /// <summary>Asks the agent to watch <paramref name="path"/>; returns the (device, inode) it marked.</summary>
    /// <exception cref="CloudFilesException">When no agent answers or it refuses.</exception>
    public static (ulong, ulong) Arm(string path)
    {
        var reply = Ask("ARM\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)))
                    ?? throw new CloudFilesException(Loc.T("linux.agent_needed"), 0);
        var parts = reply.Split('\t');
        if (parts is ["OK", var dev, var ino] && ulong.TryParse(dev, out var d) && ulong.TryParse(ino, out var i))
        {
            return (d, i);
        }
        throw new CloudFilesException(parts.Length > 1 ? parts[1] : reply, 0);
    }

    private static string? Ask(string request)
    {
        if (!File.Exists(SocketPath))
        {
            return null;
        }
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.ReceiveTimeout = s.SendTimeout = 30_000;
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            var peer = AgentSocket.PeerUid(s);
            if (peer != LinuxNative.geteuid() && peer != 0)
            {
                return null; // not our agent
            }
            s.Send(Encoding.UTF8.GetBytes(request + "\n"));
            return AgentSocket.ReadLine(s);
        }
        catch (SocketException)
        {
            return null;
        }
    }
}

/// <summary>The agent's listening socket: one line in, one line out, only from the same user (or root).</summary>
internal sealed class AgentSocket : IDisposable
{
    private readonly Socket _listener;
    private readonly string _path;

    private AgentSocket(Socket listener, string path)
    {
        _listener = listener;
        _path = path;
    }

    /// <summary>Binds <paramref name="path"/> (replacing a stale socket), owner-only, and serves requests.</summary>
    public static AgentSocket Listen(string path, Func<string, uint, string> answer)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        #pragma warning disable CA1416 // this file is compiled for Linux only
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        #pragma warning restore CA1416
        listener.Listen(16);
        var server = new AgentSocket(listener, path);
        _ = Task.Run(() => server.AcceptLoopAsync(answer));
        return server;
    }

    private async Task AcceptLoopAsync(Func<string, uint, string> answer)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() =>
            {
                using (client)
                {
                    try
                    {
                        client.ReceiveTimeout = client.SendTimeout = 10_000;
                        var peer = PeerUid(client);
                        var reply = peer == LinuxNative.geteuid() || peer == 0
                            ? answer(ReadLine(client) ?? "", peer)
                            : "ERR\tnot allowed";
                        client.Send(Encoding.UTF8.GetBytes(reply + "\n"));
                    }
                    catch (SocketException)
                    {
                    }
                }
            });
        }
    }

    /// <summary>User id of the process at the other end (SO_PEERCRED), or uint.MaxValue when unknown.</summary>
    public static uint PeerUid(Socket s)
    {
        var cred = new byte[12]; // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
        return s.GetRawSocketOption(1 /* SOL_SOCKET */, 17 /* SO_PEERCRED */, cred) == 12 ? BitConverter.ToUInt32(cred, 4) : uint.MaxValue;
    }

    /// <summary>Reads one line (at most 64 KB).</summary>
    public static string? ReadLine(Socket s)
    {
        var data = new List<byte>();
        var one = new byte[1];
        while (data.Count < 65536 && s.Receive(one) == 1)
        {
            if (one[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(data.ToArray());
            }
            data.Add(one[0]);
        }
        return null;
    }

    /// <summary>Stops listening and removes the socket file.</summary>
    public void Dispose()
    {
        _listener.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
