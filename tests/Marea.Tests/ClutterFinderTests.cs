using Marea.Core;

namespace Marea.Tests;

/// <summary>Regenerable space outside the model folders: caches, idle temporary files, crash dumps.
/// The finder only measures; these tests also check that it never follows a link and never deletes.</summary>
public sealed class ClutterFinderTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("clutter");
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Make(string rel, int kb, int ageDays = 0)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[kb << 10]);
        File.SetLastWriteTimeUtc(p, Now.AddDays(-ageDays));
        return p;
    }

    private string At(string rel) => Path.Combine(_dir, rel);

    private static ClutterPlaces Places(IReadOnlyList<CacheSpot>? caches = null, string? temp = null, string? dumps = null,
        string? hibernation = null, IReadOnlyList<string>? disks = null) =>
        new(caches ?? [], temp, dumps, hibernation, disks ?? []);

    [Fact]
    public void CachesAreMeasuredWithTheirOwnCleanCommandAndMissingOnesAreSkipped()
    {
        Make(@"pip\http\a.body", 300);
        Make(@"pip\wheels\b.whl", 200);
        Make(@"gradle\caches\c.jar", 150);
        var found = ClutterFinder.Find(Places(caches:
        [
            new CacheSpot("pip", At("pip"), "pip cache purge"),
            new CacheSpot("npm", At("missing"), "npm cache clean --force"),
            new CacheSpot("gradle", At(@"gradle\caches"), null),
        ]), minBytes: 100 << 10, nowUtc: Now);
        Assert.Equal(["pip", "gradle"], found.Select(i => i.Id));
        var pip = found[0];
        Assert.Equal(500L << 10, pip.Bytes);
        Assert.Equal(2, pip.Files);
        Assert.Equal("pip cache purge", pip.Command);
        Assert.True(pip.Reclaimable);
        Assert.Null(found[1].Command);
    }

    [Fact]
    public void TheSameFolderListedTwiceIsCountedOnce()
    {
        Make(@"cache\a.bin", 200);
        var spot = new CacheSpot("pip", At("cache"), null);
        var found = ClutterFinder.Find(Places(caches: [spot, spot with { Id = "pip2" }]), minBytes: 1, nowUtc: Now);
        Assert.Single(found);
    }

    [Fact]
    public void OnlyTemporaryEntriesNothingWroteToRecentlyAreStale()
    {
        Make(@"tmp\old-build\obj\a.o", 400, ageDays: 30);
        Make(@"tmp\old-build\obj\b.o", 100, ageDays: 20);
        Make(@"tmp\mixed\old.log", 300, ageDays: 30);
        Make(@"tmp\mixed\live.lock", 1, ageDays: 0);
        Make(@"tmp\loose.tmp", 50, ageDays: 9);
        Make(@"tmp\fresh.tmp", 50, ageDays: 1);
        var temp = ClutterFinder.Find(Places(temp: At("tmp")), staleDays: 7, minBytes: 400 << 10, nowUtc: Now).Single();
        Assert.Equal(ClutterKind.StaleTemp, temp.Kind);
        // "mixed" has a file written today: the whole folder is in use and is left out (it is small).
        Assert.Equal([At(@"tmp\old-build"), At(@"tmp\loose.tmp")], temp.Parts.Select(p => p.Path));
        Assert.Equal(550L << 10, temp.Bytes);
        Assert.Equal(3, temp.Files);
        Assert.Equal(Now.AddDays(-9), temp.NewestUtc);
    }

    [Fact]
    public void ALargeBusyFolderIsJudgedEntryByEntryButOnlyTwoLevelsDeep()
    {
        // tmp\agent is written to today, yet holds abandoned work folders next to the live one.
        Make(@"tmp\agent\session-1\target\big.rlib", 700, ageDays: 40);
        Make(@"tmp\agent\session-2\target\big.rlib", 600, ageDays: 12);
        Make(@"tmp\agent\session-3\now.txt", 400, ageDays: 0);
        Make(@"tmp\agent\session-3\deep\old.txt", 900, ageDays: 40);
        var temp = ClutterFinder.Find(Places(temp: At("tmp")), staleDays: 7, minBytes: 500 << 10, nowUtc: Now).Single();
        Assert.Equal([At(@"tmp\agent\session-1"), At(@"tmp\agent\session-2")], temp.Parts.Select(p => p.Path));
        Assert.Equal(1300L << 10, temp.Bytes);
    }

    [Fact]
    public void HibernationAndVirtualDisksAreReportedButNeverCountedAsReclaimable()
    {
        Make("hiberfil.sys", 800);
        Make(@"vm\distro-a\ext4.vhdx", 900);
        Make(@"vm\distro-b\ext4.vhdx", 300);
        Make(@"vm\distro-b\notes.txt", 300);
        Make(@"dumps\app.dmp", 200);
        var found = ClutterFinder.Find(Places(dumps: At("dumps"), hibernation: At("hiberfil.sys"), disks: [At("vm"), At("nowhere")]),
            minBytes: 100 << 10, nowUtc: Now);
        Assert.Equal(["crash_dumps", "virtual_disks", "hibernation"], found.Select(i => i.Id));
        Assert.True(found[0].Reclaimable);
        var disks = found[1];
        Assert.False(disks.Reclaimable);
        Assert.Equal(1200L << 10, disks.Bytes);
        Assert.Equal(2, disks.Files);
        Assert.Equal(At("vm"), disks.Path);
        Assert.Equal(At(@"vm\distro-a\ext4.vhdx"), disks.Parts[0].Path);
        Assert.False(found[2].Reclaimable);
        Assert.Equal("powercfg /h off", found[2].Command);
    }

    [Fact]
    public void ItemsBelowTheMinimumAreLeftOutAndNothingIsEverDeleted()
    {
        var small = Make(@"pip\a.body", 10);
        var old = Make(@"tmp\old.tmp", 10, ageDays: 90);
        var found = ClutterFinder.Find(Places(caches: [new CacheSpot("pip", At("pip"), null)], temp: At("tmp")), minBytes: 1 << 20, nowUtc: Now);
        Assert.Empty(found);
        Assert.True(File.Exists(small));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void JunctionsAreNotFollowed()
    {
        Make(@"outside\precious.bin", 500, ageDays: 90);
        Make(@"tmp\real.tmp", 200, ageDays: 90);
        try
        {
            Directory.CreateSymbolicLink(At(@"tmp\link"), At("outside"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // creating links needs a privilege this machine does not grant
        }
        var temp = ClutterFinder.Find(Places(temp: At("tmp")), minBytes: 1, nowUtc: Now).Single();
        Assert.Equal(200L << 10, temp.Bytes);
        Assert.Equal([At(@"tmp\real.tmp")], temp.Parts.Select(p => p.Path));
    }

    [Fact]
    public void OverrideVariablesReplaceTheDefaultCacheFolders()
    {
        var env = new Dictionary<string, string> { ["PIP_CACHE_DIR"] = @"X:\pipcache", ["CARGO_HOME"] = @"X:\cargo" };
        var spots = ClutterPlaces.KnownCaches(@"H:\home", @"H:\local", k => env.GetValueOrDefault(k)).ToList();
        Assert.Equal([@"X:\pipcache"], spots.Where(s => s.Id == "pip").Select(s => s.Path));
        Assert.Equal([Path.Combine(@"X:\cargo", "registry")], spots.Where(s => s.Id == "cargo").Select(s => s.Path));
        Assert.Equal(2, spots.Count(s => s.Id == "npm"));
        Assert.All(spots.Where(s => s.Id is "pip" or "npm" or "uv" or "nuget" or "go"), s => Assert.NotNull(s.Command));
    }
}
