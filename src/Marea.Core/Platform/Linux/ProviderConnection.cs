using System.Security.Cryptography;
using Marea.Core;

namespace Marea.CloudFiles;

/// <summary>Linux version of a request for the data of a freed file. The program that opened it waits until
/// the request completes, so the whole file is asked for at once and checked against its sha256 before the
/// open is allowed. The first block is written last: if the agent dies half way, the notice is still at the
/// start of the file and no loader can mistake the half file for a model.</summary>
public sealed class FetchRequest
{
    private const int HeadSize = 4096;
    private readonly int _fd;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _head;
    private long _next;
    private bool _failed;
    private bool _verified;

    internal FetchRequest(int fd, byte[] identity, string fullPath, long size, uint processId, string? processImage)
    {
        _fd = fd;
        Identity = identity;
        Path = fullPath.TrimStart('/');
        FileSize = size;
        ProcessId = processId;
        ProcessImage = processImage;
        _head = new byte[(int)Math.Min(HeadSize, size)];
    }

    /// <summary>Always 0: there is no optional range on Linux.</summary>
    public long OptionalFileOffset => 0;
    /// <summary>Always 0.</summary>
    public long OptionalLength => 0;
    /// <summary>Process that opened the file.</summary>
    public uint ProcessId { get; }
    /// <summary>Its executable, when readable.</summary>
    public string? ProcessImage { get; }
    /// <summary>Identity bytes stored when the file was freed.</summary>
    public byte[] Identity { get; }
    /// <summary>Path without the leading '/': "/" + Path is the absolute path, as "C:" + Path is on Windows.</summary>
    public string Path { get; }
    /// <summary>Logical size of the file.</summary>
    public long FileSize { get; }
    /// <summary>Always 0: the whole file is needed.</summary>
    public long RequiredOffset => 0;
    /// <summary>The whole file.</summary>
    public long RequiredLength => FileSize;
    /// <summary>Cancelled when the agent stops.</summary>
    public CancellationToken Cancellation => _cts.Token;
    internal readonly CancellationTokenSource _cts = new();

    /// <summary>Delivers the next block. Blocks must arrive in order, from offset 0.</summary>
    public void Transfer(long offset, ReadOnlySpan<byte> data)
    {
        if (offset == 0 && _next > 0)
        {
            // The previous source failed half way and the next one starts over.
            _hash.GetHashAndReset();
            _next = 0;
        }
        _verified = false;
        if (offset != _next || offset + data.Length > FileSize)
        {
            throw new IOException($"out of order block at {offset}");
        }
        _hash.AppendData(data);
        var inHead = (int)Math.Clamp(_head.Length - offset, 0, data.Length);
        if (inHead > 0)
        {
            data[..inHead].CopyTo(_head.AsSpan((int)offset));
        }
        if (inHead < data.Length)
        {
            LinuxNative.WriteAt(_fd, data[inHead..], offset + inHead);
        }
        _next += data.Length;
        if (_next == FileSize)
        {
            // Checked here, not after: a wrong source then fails like any broken source and the next one is tried.
            _verified = Convert.ToHexStringLower(_hash.GetHashAndReset()) == ContentHash.FromIdentity(Identity);
            if (!_verified)
            {
                _next = 0;
                throw new IOException(Loc.T("hyd.fingerprint_differs"));
            }
        }
    }

    /// <summary>No progress channel on Linux: the opening program just waits.</summary>
    public void ReportProgress(long total, long completed)
    {
    }

    /// <summary>Marks the request failed: the opening program receives a permission error.</summary>
    public void Fail(long offset, long length) => _failed = true;

    /// <summary>True when every byte arrived and hashes to the identity; then makes the data durable and
    /// writes the first block (the notice disappears only now).</summary>
    internal bool Complete()
    {
        if (_failed || !_verified)
        {
            return false;
        }
        LinuxNative.Flush(_fd);
        LinuxNative.WriteAt(_fd, _head, 0);
        LinuxNative.Flush(_fd);
        return true;
    }
}

/// <summary>Linux version of a served folder. One fanotify group serves every folder (see
/// <see cref="FanotifyServer"/>); connecting a folder arms the freed files already in it.</summary>
public sealed class ProviderConnection : IDisposable
{
    private ProviderConnection(string root) => Root = root;

    /// <summary>Folder this connection serves.</summary>
    public string Root { get; }

    /// <summary>Arms the freed files under <paramref name="root"/> when this process is the agent. In any other
    /// process it does nothing: only the agent, started with <see cref="FanotifyServer.Start"/>, can serve,
    /// so a short-lived command never leaves freed files unwatched when it exits.</summary>
    public static ProviderConnection Connect(string root, Func<FetchRequest, Task> handler)
    {
        FanotifyServer.Current?.ArmUnder(root);
        return new ProviderConnection(root);
    }

    /// <summary>Nothing to release per folder: the marks belong to the agent's single group.</summary>
    public void Dispose()
    {
    }
}
