using Marea.Core;

namespace Marea.Cli;

/// <summary>"clutter": space that is not a model but can be produced again (package caches, idle
/// temporary files, crash dumps) and space that needs a decision (hibernation file, virtual disks).
/// Read-only: Marea never deletes; each line names the command of the tool that owns the data.</summary>
internal static class ClutterCommands
{
    /// <summary>Largest sub-entries printed under an item in text mode.</summary>
    private const int PartsShown = 5;

    public static int Clutter(bool json, int staleDays)
    {
        var items = ClutterFinder.Find(ClutterPlaces.Current(), staleDays);
        var reclaimable = items.Where(i => i.Reclaimable).ToList();
        var other = items.Where(i => !i.Reclaimable).ToList();
        if (json)
        {
            CliOutput.Json("marea.clutter/1", new
            {
                staleDays,
                reclaimableBytes = reclaimable.Sum(i => i.Bytes),
                otherBytes = other.Sum(i => i.Bytes),
                items = items.Select(i => new
                {
                    id = i.Id,
                    kind = i.Kind,
                    path = i.Path,
                    sizeBytes = i.Bytes,
                    files = i.Files,
                    newest = i.NewestUtc,
                    command = i.Command,
                    reclaimable = i.Reclaimable,
                    parts = i.Parts.Select(p => new { path = p.Path, sizeBytes = p.Bytes, newest = p.NewestUtc }),
                }),
            });
            return Exit.Ok;
        }
        if (items.Count == 0)
        {
            Console.WriteLine(Loc.T("clutter.none"));
            return Exit.Ok;
        }
        foreach (var i in reclaimable)
        {
            Print(i, staleDays);
        }
        if (reclaimable.Count > 0)
        {
            Console.WriteLine("\n" + Loc.T("clutter.total", CliOutput.Gb(reclaimable.Sum(i => i.Bytes)), reclaimable.Count));
        }
        if (other.Count > 0)
        {
            Console.WriteLine("\n" + Loc.T("clutter.other"));
            foreach (var i in other)
            {
                Print(i, staleDays);
            }
        }
        return Exit.Ok;
    }

    private static void Print(ClutterItem i, int staleDays)
    {
        Console.WriteLine($"{CliOutput.Gb(i.Bytes),9}  {Loc.T("clutter.kind." + Key(i.Kind)),-24} {CliOutput.Short(i.Path)}");
        Console.WriteLine($"{"",9}  {Hint(i, staleDays)}");
        foreach (var p in i.Parts.Take(PartsShown))
        {
            Console.WriteLine($"{"",9}    {CliOutput.Gb(p.Bytes),9}  {CliOutput.Short(p.Path)}");
        }
        if (i.Parts.Count > PartsShown)
        {
            Console.WriteLine($"{"",9}    {Loc.T("clutter.more", i.Parts.Count - PartsShown)}");
        }
    }

    /// <summary>What to do about an item: the owner tool's command, or the rule that makes it safe.</summary>
    private static string Hint(ClutterItem i, int staleDays) => i.Kind switch
    {
        ClutterKind.PackageCache => i.Command is { } c
            ? Loc.T("clutter.hint.command", i.Id, c)
            : Loc.T("clutter.hint.no_command", i.Id),
        ClutterKind.StaleTemp => Loc.T("clutter.hint.temp", i.Parts.Count, staleDays),
        ClutterKind.CrashDumps => Loc.T("clutter.hint.crash_dumps"),
        ClutterKind.Hibernation => Loc.T("clutter.hint.hibernation", i.Command ?? ""),
        _ => Loc.T("clutter.hint.virtual_disk", i.Files),
    };

    /// <summary>Translation key suffix of a kind (clutter.kind.package_cache, ...).</summary>
    public static string Key(ClutterKind k) => k switch
    {
        ClutterKind.PackageCache => "package_cache",
        ClutterKind.StaleTemp => "stale_temp",
        ClutterKind.CrashDumps => "crash_dumps",
        ClutterKind.Hibernation => "hibernation",
        _ => "virtual_disk",
    };
}
