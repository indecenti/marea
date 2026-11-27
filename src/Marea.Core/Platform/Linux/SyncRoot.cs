using Marea.Core;

namespace Marea.CloudFiles;

/// <summary>Linux version of folder registration: a managed folder carries the <c>user.marea.root</c>
/// extended attribute, set only after proving its file system can hold freed files.</summary>
public static class SyncRoot
{
    internal const string RootAttr = "user.marea.root";

    /// <summary>Provider name (same as on Windows).</summary>
    public const string ProviderName = "Marea";

    /// <summary>Marks <paramref name="root"/> as managed after checking its file system.</summary>
    /// <exception cref="CloudFilesException">When the file system lacks extended attributes or hole punching.</exception>
    public static void Register(string root, string version)
    {
        if (Probe(root) is { } why)
        {
            throw new CloudFilesException(why, 0);
        }
        LinuxNative.SetXattr(root, RootAttr, System.Text.Encoding.ASCII.GetBytes(version));
    }

    /// <summary>Removes the mark. Call only when no freed file is left under the folder.</summary>
    public static void Unregister(string root) => LinuxNative.RemoveXattr(root, RootAttr);

    /// <summary>True when <paramref name="path"/> is, or lies inside, a folder marked by Marea.</summary>
    public static bool IsMareaRoot(string path)
    {
        for (var d = Path.GetFullPath(path); !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d))
        {
            if (LinuxNative.HasXattr(d, RootAttr))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Linux has no cloud sync roots of other providers to collide with.</summary>
    public static bool IsForeignRoot(string path) => false;

    /// <summary>Null when files under <paramref name="root"/> can be freed now; otherwise the reason, as a
    /// translation key. On Linux the agent must be running, or a freed file could not come back.</summary>
    public static string? NotReadyKey(string root) =>
        FanotifyServer.Current is not null || AgentLink.Ping() ? null : "linux.agent_needed";

    /// <summary>Writes, sparsifies and labels a scratch file in <paramref name="folder"/>: null when all
    /// works, otherwise the reason in the user's language.</summary>
    public static string? Probe(string folder)
    {
        var path = Path.Combine(folder, $".marea-probe-{Guid.NewGuid():N}");
        try
        {
            using var h = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.DeleteOnClose);
            var fd = (int)h.DangerousGetHandle();
            var block = new byte[8192];
            Array.Fill(block, (byte)0x5A);
            LinuxNative.WriteAt(fd, block, 0);
            LinuxNative.SetXattr(fd, Placeholder.IdAttr, block.AsSpan(0, 32));
            return LinuxNative.PunchHole(fd, block.Length) == 0 ? null : Loc.T("linux.fs_unsupported");
        }
        catch (Exception e) when (e is IOException or CloudFilesException)
        {
            return Loc.T("linux.fs_unsupported");
        }
        catch (UnauthorizedAccessException e)
        {
            return e.Message;
        }
    }
}

/// <summary>A failure of the platform layer, with a readable message (same type as on Windows).</summary>
public sealed class CloudFilesException : Exception
{
    /// <summary>errno (or 0) behind the failure.</summary>
    public int HResultCode { get; }

    /// <summary>Creates the exception.</summary>
    public CloudFilesException(string message, int code) : base(message) => HResultCode = code;
}
