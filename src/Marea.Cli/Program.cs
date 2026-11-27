using Marea.Cli;
using Marea.Core;

// Marea CLI: same engine as the desktop app, for scripts, power users and AI agents.
// Destructive actions print the plan and need --yes (exit 2 without it). --json gives a versioned
// JSON document. MAREA_READONLY=1 refuses every change (exit 5). MAREA_DATA overrides the data folder.

var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var json = args.Contains("--json");
var positional = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
var readOnly = Environment.GetEnvironmentVariable("MAREA_READONLY") is "1" or "true";
string[] changing = ["add", "remove", "evaporate", "free", "undo", "restore", "restore-all", "replicate", "agent"];

if (cmd == "check")
{
    Loc.SetLanguage(Environment.GetEnvironmentVariable("MAREA_LANG"));
    if (positional.Count == 0)
    {
        Console.Error.WriteLine(Loc.T("cli.missing_arg"));
        return Exit.Usage;
    }
    return ModelCommands.Check(positional, json);
}

using var engine = new MareaEngine(Environment.GetEnvironmentVariable("MAREA_DATA"));
Loc.SetLanguage(Environment.GetEnvironmentVariable("MAREA_LANG") ?? engine.Settings.Language);
var progress = new Progress<ScanProgress>(p =>
{
    if (json)
    {
        return;
    }
    var pct = p.Total > 0 ? $" {100.0 * p.Done / p.Total,5:0.0}%" : "";
    Console.Error.Write($"\r{p.Phase}{pct}  {CliOutput.Short(p.Path),-70}");
});

if (readOnly && changing.Contains(cmd))
{
    Console.Error.WriteLine(Loc.T("cli.read_only"));
    return Exit.ReadOnly;
}

try
{
    return await Dispatch();
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return Exit.Usage;
}

async Task<int> Dispatch()
{
    switch (cmd)
    {
        case "status":
            Status();
            return Exit.Ok;
        case "models":
            {
                var i = Array.IndexOf(args, "--unused");
                int? days = i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var d) && d >= 0 ? d : null;
                return ModelCommands.Models(engine, json, days, args.Contains("--rollback"));
            }
        case "add":
            {
                var why = engine.AddRoot(Arg(1));
                Console.WriteLine(why ?? Loc.T("cli.added"));
                return why is null ? Exit.Ok : Exit.Error;
            }
        case "remove":
            {
                engine.StartProvider();
                var why = await engine.RemoveRootAsync(Arg(1), progress);
                Console.WriteLine(why ?? Loc.T("cli.removed"));
                return why is null ? Exit.Ok : Exit.Error;
            }
        case "scan":
            var hashed = await engine.ScanAsync(progress);
            await engine.RefreshSourcesAsync(progress);
            if (!json)
            {
                Console.Error.WriteLine();
                Console.WriteLine(Loc.T("cli.scanned", hashed));
            }
            Status();
            return Exit.Ok;
        case "plan":
            PrintPlan(engine.Plan(GbArg(), args.Contains("--instant")));
            return Exit.Ok;
        case "evaporate":
            {
                var plan = engine.Plan(GbArg());
                if (positional.Count > 0)
                {
                    var only = Path.GetFullPath(positional[0]);
                    plan = plan.Where(p => string.Equals(p.File.Path, only, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                return await EvaporatePlan(plan, null);
            }
        case "free":
            {
                // Quick mode: --all for everything safe, --gb N for an amount; --instant keeps only
                // files that come back offline in seconds (local copy or recipe).
                var plan = engine.Plan(args.Contains("--all") || args.Contains("--rollback") ? 0 : GbArg() > 0 ? GbArg() : 10L << 30, args.Contains("--instant"));
                if (args.Contains("--rollback"))
                {
                    // Only the copies Ollama keeps for a downgrade; the gate still decides file by file.
                    var rollback = engine.OllamaRollbackFiles().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    plan = plan.Where(p => rollback.Contains(p.File.Path)).ToList();
                }
                return await EvaporatePlan(plan, "quick-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            }
        case "undo":
            {
                var last = engine.LastUndoableBatch();
                if (last is null)
                {
                    Console.WriteLine(Loc.T("cli.nothing_to_undo"));
                    return Exit.Ok;
                }
                engine.StartProvider();
                var why = await engine.UndoBatchAsync(last.Value.Id, progress);
                Console.WriteLine(why ?? Loc.T("cli.undone", last.Value.Files, CliOutput.Gb(last.Value.Bytes)));
                return why is null ? Exit.Ok : Exit.Error;
            }
        case "restore":
            {
                engine.StartProvider();
                var rec = engine.Ledger.Get(Path.GetFullPath(Arg(1)));
                var why = rec is null ? Loc.T("cli.not_tracked") : await engine.BringBackAsync([rec], progress);
                Console.WriteLine(why ?? Loc.T("cli.restored"));
                return why is null ? Exit.Ok : Exit.Error;
            }
        case "restore-all":
            {
                if (!args.Contains("--yes"))
                {
                    Console.WriteLine(Loc.T("cli.restore_all_need_yes"));
                    return Exit.NeedsConfirmation;
                }
                if (!await TakeProviderRoleAsync())
                {
                    Console.WriteLine(Loc.T("cli.close_app_first"));
                    return Exit.NeedsConfirmation;
                }
                var problem = await engine.RestoreEverythingAsync(progress);
                Console.Error.WriteLine();
                Console.WriteLine(problem ?? Loc.T("cli.restored_all"));
                if (problem is null && args.Contains("--uninstall"))
                {
                    RemoveStartupEntry();
                }
                return problem is null ? Exit.Ok : Exit.Error;
            }
        case "replicate":
            engine.StartProvider();
            var unique = engine.Ledger.Files().Where(f => f.Class == Recoverability.Unique && f.Residency == Residency.Local).ToList();
            Console.WriteLine(Loc.T("cli.replicated", await engine.ReplicateAsync(unique, progress)));
            return Exit.Ok;
        case "watch":
            return await Watch();
        case "journal":
            Journal(positional.Count > 0 && int.TryParse(positional[0], out var n) ? n : 30);
            return Exit.Ok;
        case "agent":
            return await Serve();
        case "help" or "--help" or "-h":
            Console.WriteLine(Loc.T("cli.help"));
            return Exit.Ok;
        default:
            Console.WriteLine(Loc.T("cli.help"));
            return Exit.Usage;
    }
}

async Task<int> EvaporatePlan(List<PlanItem> plan, string? batch)
{
    PrintPlan(plan);
    if (!args.Contains("--yes"))
    {
        Console.Error.WriteLine(Loc.T("cli.need_yes"));
        return Exit.NeedsConfirmation;
    }
    engine.StartProvider();
    var (done, kept) = await engine.EvaporateAsync(plan.Select(p => p.File), progress, default, batch);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Loc.T("cli.evaporated", done) + (batch is null ? "" : " " + Loc.T("cli.undo_hint")));
    foreach (var (f, why) in kept)
    {
        Console.Error.WriteLine($"  {Loc.T("cli.kept")}: {CliOutput.Clean(f.Path)} -> {CliOutput.Clean(why)}");
    }
    return Exit.Ok;
}

// Checks public sources; rescues and protects unless read-only or --no-rescue.
// Exit code: 0 nothing at risk, 3 models at risk, 4 a model cannot come back.
async Task<int> Watch()
{
    var act = !readOnly && !args.Contains("--no-rescue");
    if (act)
    {
        engine.StartProvider();
    }
    var o = await engine.WatchAsync(act, progress);
    if (json)
    {
        CliOutput.Json("marea.watch/1", new
        {
            sourcesChecked = o.Report.SourcesChecked,
            changed = o.Report.Changed,
            rescued = o.Rescued,
            @protected = o.Protected,
            findings = o.Remaining.Select(f => new
            {
                path = f.File.Path,
                residency = f.File.Residency,
                state = f.State,
                action = f.Action,
                detail = Loc.Render(f.Detail),
            }),
        });
    }
    else
    {
        Console.Error.WriteLine();
        Console.WriteLine(Loc.T("cli.watch_summary", o.Report.SourcesChecked, o.Rescued, o.Protected));
        foreach (var f in o.Remaining)
        {
            Console.WriteLine($"  {Loc.T("risk." + f.Action.ToString().ToLowerInvariant()),-24} {CliOutput.Short(f.File.Path)}");
            Console.WriteLine($"  {"",-24} {CliOutput.Clean(Loc.Render(f.Detail))}");
        }
    }
    return o.Remaining.Any(f => f.Action == WatchAction.Lost) ? Exit.Dangerous
        : o.Remaining.Count > 0 ? Exit.Suspicious : Exit.Ok;
}

async Task<int> Serve()
{
    if (!engine.StartProvider())
    {
        Console.WriteLine(Loc.T("cli.already_serving"));
        return Exit.Error;
    }
    Console.WriteLine(Loc.T("cli.agent_listening"));
    engine.Hydration.MassRead += a =>
        Console.WriteLine(Loc.T("reader.tray_text", CliOutput.Clean(a.Name), a.Files, CliOutput.Gb(a.Bytes)));
    engine.Hydration.Progress += e =>
    {
        if (e.Finished)
        {
            Console.WriteLine($"{(e.Error is null ? Loc.T("cli.returned") : Loc.T("cli.error"))}: {CliOutput.Clean(e.Path)} {e.Error}");
        }
    };
    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    while (!stop.Task.IsCompleted)
    {
        if (engine.Settings.AutoTide)
        {
            var freed = await engine.RunTideAsync(progress);
            if (freed > 0)
            {
                Console.WriteLine("\n" + Loc.T("cli.tide_freed", CliOutput.Gb(freed)));
            }
        }
        await Task.WhenAny(stop.Task, Task.Delay(TimeSpan.FromMinutes(10)));
    }
    return Exit.Ok;
}

// The desktop app holds the provider role; Windows cannot detach a folder while it is connected.
// Ask it to quit (same user session) and wait for the role to free up.
async Task<bool> TakeProviderRoleAsync()
{
    if (engine.StartProvider())
    {
        return true;
    }
    if (EventWaitHandle.TryOpenExisting(@"Local\Marea.App.Quit.v1", out var quit))
    {
        quit.Set();
        quit.Dispose();
    }
    for (var i = 0; i < 40; i++)
    {
        await Task.Delay(500);
        if (engine.StartProvider())
        {
            return true;
        }
    }
    return false;
}

static void RemoveStartupEntry()
{
    using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
    if (k?.GetValue("Marea") is not null)
    {
        k.DeleteValue("Marea");
    }
}

string Arg(int i) => args.Length > i && !args[i].StartsWith("--", StringComparison.Ordinal)
    ? args[i]
    : throw new ArgumentException(Loc.T("cli.missing_arg"));

long GbArg()
{
    var i = Array.IndexOf(args, "--gb");
    if (i < 0 || i + 1 >= args.Length)
    {
        return 0;
    }
    var text = args[i + 1];
    const System.Globalization.NumberStyles num = System.Globalization.NumberStyles.Float;
    return double.TryParse(text, num, System.Globalization.CultureInfo.InvariantCulture, out var gb)
           || double.TryParse(text, num, System.Globalization.CultureInfo.CurrentCulture, out gb)
        ? (long)(gb * (1L << 30))
        : throw new ArgumentException(Loc.T("cli.missing_arg"));
}

void Status()
{
    var files = engine.Ledger.Files();
    var allowed = engine.Plan(0).Sum(p => p.File.Size);
    var gone = files.Where(f => f.Residency == Residency.Evaporated).ToList();
    if (json)
    {
        CliOutput.Json("marea.status/1", new
        {
            volumes = engine.Volumes().Select(v => new { v.Volume, freeBytes = v.FreeBytes, totalBytes = v.TotalBytes, targetBytes = v.TargetBytes }),
            files = files.Count,
            bytes = files.Sum(f => f.Size),
            byClass = files.GroupBy(f => f.Class).ToDictionary(g => g.Key.ToString().ToLowerInvariant(), g => new { files = g.Count(), bytes = g.Sum(f => f.Size) }),
            evaporated = new { files = gone.Count, bytes = gone.Sum(f => f.Size) },
            freeableBytes = allowed,
            sourcesAtRisk = engine.SourceRisks().Count,
            lastWatch = engine.Settings.LastWatchUtc == default ? (DateTime?)null : engine.Settings.LastWatchUtc,
            readOnly,
        });
        return;
    }
    foreach (var v in engine.Volumes())
    {
        Console.WriteLine(Loc.T("cli.volume", v.Volume, CliOutput.Gb(v.FreeBytes), CliOutput.Gb(v.TotalBytes), CliOutput.Gb(v.TargetBytes)));
    }
    Console.WriteLine("\n" + Loc.T("cli.ledger", files.Count, CliOutput.Gb(files.Sum(f => f.Size))));
    foreach (var g in files.GroupBy(f => f.Class).OrderByDescending(g => g.Key))
    {
        Console.WriteLine($"  {Label(g.Key),-14} {g.Count(),4}  {CliOutput.Gb(g.Sum(f => f.Size)),10}");
    }
    Console.WriteLine($"  {Loc.T("res.evaporated"),-14} {gone.Count,4}  {CliOutput.Gb(gone.Sum(f => f.Size)),10}");
    Console.WriteLine("\n" + Loc.T("cli.freeable", CliOutput.Gb(allowed)));
}

void PrintPlan(List<PlanItem> plan)
{
    if (json)
    {
        CliOutput.Json("marea.plan/1", new
        {
            items = plan.Select(p => new
            {
                path = p.File.Path,
                sizeBytes = p.File.Size,
                @class = p.File.Class,
                returnPath = p.Path,
                returnSeconds = double.IsInfinity(p.RebuildSeconds) ? (double?)null : Math.Round(p.RebuildSeconds, 1),
                instant = p.Instant,
            }),
            totalBytes = plan.Sum(p => p.File.Size),
        });
        return;
    }
    foreach (var p in plan)
    {
        Console.WriteLine($"{p.File.SizeGb,8:0.00} GB  {Dur(p.RebuildSeconds),9}  {Label(p.File.Class),-13} {CliOutput.Short(p.File.Path)}");
    }
    Console.WriteLine(Loc.T("plan.total", CliOutput.Gb(plan.Sum(p => p.File.Size)), plan.Count));
}

void Journal(int count)
{
    var entries = engine.Ledger.Journal(count);
    if (json)
    {
        CliOutput.Json("marea.journal/1", new
        {
            entries = entries.Select(j => new { at = j.AtUtc, action = j.Action, text = Loc.Render(j.Action), j.Path, j.Bytes, j.Ok, detail = Loc.Render(j.Detail) }),
        });
        return;
    }
    foreach (var j in entries)
    {
        Console.WriteLine($"{j.AtUtc.ToLocalTime():g}  {(j.Ok ? " " : "!")} {Loc.Render(j.Action),-26} {CliOutput.Gb(j.Bytes),9}  "
            + $"{CliOutput.Short(j.Path)}  {CliOutput.Clean(Loc.Render(j.Detail))}");
    }
}

static string Label(Recoverability r) => Loc.T("class." + r.ToString().ToLowerInvariant());
static string Dur(double s) => double.IsInfinity(s) ? "-" : s < 90 ? $"{s:0} s" : $"{s / 60:0} min";
