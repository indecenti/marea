using Marea.CloudFiles;
using Microsoft.Win32.SafeHandles;

namespace Marea.Core;

/// <summary>Takes one file off the disk, only after re-proving everything at the last moment:
/// the gate still allows it, a source is alive right now, and the bytes still hash to the ledger
/// value under an exclusive handle (so nothing can change them between check and evaporation).</summary>
public sealed class Evaporator(Ledger ledger, MareaSettings settings, HuggingFaceClient hub, OllamaRegistry ollama, SafetyGate gate)
{
    /// <summary>Provider version written in the sync root registration.</summary>
    public const string Version = "0.1";

    /// <summary>Evaporates <paramref name="f"/>; returns null on success or the reason it was kept.</summary>
    public async Task<string?> EvaporateAsync(FileRecord f, IProgress<ScanProgress>? progress = null, CancellationToken ct = default,
        string? batch = null)
    {
        var all = ledger.Files();
        var verdict = gate.Evaluate(f, all, gate.PinnedPaths(all), [], DateTime.UtcNow);
        if (!verdict.Allowed)
        {
            return Keep(f, verdict.Blockers[0].Key, verdict.Blockers[0].Args);
        }
        if (!await AnySourceAliveAsync(f, ct))
        {
            return Keep(f, "ev.no_source_alive");
        }
        if (Placeholder.GetState(f.Path) is var state && state is PlaceholderState.Dehydrated or PlaceholderState.Modified)
        {
            return Keep(f, "ev.unexpected_state", state);
        }
        if (SyncRoot.NotReadyKey(f.Root) is { } notReady)
        {
            return Keep(f, notReady);
        }
        if (!SyncRoot.IsMareaRoot(f.Root))
        {
            try
            {
                SyncRoot.Register(f.Root, Version);
            }
            catch (CloudFilesException e)
            {
                return Keep(f, "ev.platform_refused", e.Message);
            }
        }
        SafeFileHandle h;
        try
        {
            h = File.OpenHandle(f.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Keep(f, "ev.in_use");
        }
        using (h)
        {
            if (FileSystemFacts.HardLinkCount(h) > 1)
            {
                return Keep(f, "ev.hardlinks");
            }
            if (RandomAccess.GetLength(h) != f.Size || File.GetLastWriteTimeUtc(f.Path).Ticks != f.MTimeTicks)
            {
                return Changed(f);
            }
            var hp = new Progress<long>(b => progress?.Report(new ScanProgress(Loc.T("phase.verify_bits"), f.Path, b, f.Size)));
            if (await ContentHash.Sha256Async(h, hp, ct) != f.Sha256)
            {
                return Changed(f);
            }
            var identity = ContentHash.ToIdentity(f.Sha256);
            try
            {
                if (state == PlaceholderState.NormalFile)
                {
                    Placeholder.ConvertAndDehydrate(h, identity);
                }
                else
                {
                    Placeholder.Dehydrate(h);
                }
            }
            catch (IOException)
            {
                // Linux: another program has the file open or mapped (Windows refuses at the open above).
                return Keep(f, "ev.in_use");
            }
            catch (CloudFilesException e)
            {
                return Keep(f, "ev.platform_refused", e.Message);
            }
        }
        f.Residency = Residency.Evaporated;
        f.EvaporatedUtc = DateTime.UtcNow;
        f.Batch = batch;
        ledger.Upsert(f);
        ledger.Log("j.evaporated", f.Path, f.Size, true, f.SourceLabel);
        return null;
    }

    /// <summary>True when at least one independent source answers right now.</summary>
    private async Task<bool> AnySourceAliveAsync(FileRecord f, CancellationToken ct)
    {
        var copy = ledger.WithSha(f.Sha256).Any(o => !string.Equals(o.Path, f.Path, StringComparison.OrdinalIgnoreCase)
                                                    && o.Residency == Residency.Local && StillAsRecorded(o));
        if (copy)
        {
            return true;
        }
        if (ledger.GetRecipe(f.Sha256) is { } json && Recipe.FromJson(json).SourcesUnchanged())
        {
            return true;
        }
        foreach (var s in ledger.SourcesOf(f.Sha256))
        {
            if (s.Kind == SourceKind.Mirror && settings.MirrorRoot is { } m && new MirrorStore(m).Has(s.Sha256, s.Size))
            {
                return true;
            }
            if (s.Kind == SourceKind.Ollama)
            {
                if (await ollama.VerifyAsync(OllamaBlob.Parse(s.Locator, s.Size), ct))
                {
                    ledger.PutSource(s with { VerifiedUtc = DateTime.UtcNow });
                    return true;
                }
                ledger.DropSource(s);
                ledger.Log("j.source_gone", s.Locator, s.Size, false, Loc.Code("ev.public_changed"));
            }
            if (s.Kind == SourceKind.HuggingFace)
            {
                var ok = await hub.VerifyAsync(HubFile.Parse(s.Locator, s.Sha256, s.Size), ct);
                if (ok)
                {
                    ledger.PutSource(s with { VerifiedUtc = DateTime.UtcNow });
                    return true;
                }
                ledger.DropSource(s);
                ledger.Log("j.source_gone", s.Locator, s.Size, false, Loc.Code("ev.public_changed"));
            }
        }
        return false;
    }

    /// <summary>True when a tracked file still has the size and write time it had when hashed.</summary>
    public static bool StillAsRecorded(FileRecord o)
    {
        var fi = new FileInfo(o.Path);
        return fi.Exists && fi.Length == o.Size && fi.LastWriteTimeUtc.Ticks == o.MTimeTicks;
    }

    private string Keep(FileRecord f, string key, params object?[] args)
    {
        ledger.Log("j.kept", f.Path, f.Size, true, Loc.Code(key, args));
        return Loc.T(key, args);
    }

    private string Changed(FileRecord f)
    {
        f.Residency = Residency.Changed;
        ledger.Upsert(f);
        return Keep(f, "ev.changed");
    }
}
