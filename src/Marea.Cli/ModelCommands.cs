using Marea.Core;

namespace Marea.Cli;

/// <summary>"models" (inventory of the managed folders) and "check" (safety of any file, before loading it).</summary>
internal static class ModelCommands
{
    private static readonly HashSet<string> CheckExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".safetensors", ".sft", ".gguf", ".ckpt", ".pt", ".pth", ".bin", ".onnx", ".pkl", ".pickle", ".joblib",
    };

    /// <summary>Lists every model in the ledger with format, precision, size, residency and safety.</summary>
    public static int Models(MareaEngine engine, bool json, int? unusedDays = null, bool rollbackOnly = false)
    {
        var models = engine.Models();
        if (rollbackOnly)
        {
            models = models.Where(m => m.RollbackFiles.Count > 0).ToList();
        }
        if (unusedDays is int days)
        {
            models = models.Where(m => m.UnusedFor(days, DateTime.UtcNow)).ToList();
        }
        if (json)
        {
            CliOutput.Json("marea.models/1", new
            {
                models = models.Select(m => new
                {
                    name = m.Name,
                    kind = m.Kind,
                    folder = m.Folder,
                    formats = m.Formats,
                    precisions = m.Precisions,
                    architecture = m.Architecture,
                    parameters = m.Parameters,
                    sizeBytes = m.Size,
                    evaporatedBytes = m.EvaporatedBytes,
                    state = m.State,
                    lastUsed = m.LastUsedUtc,
                    safety = m.Safety,
                    hubScan = m.HubScan,
                    rollbackBytes = m.RollbackFiles.Sum(f => f.Size),
                    findings = m.Findings,
                    files = m.Files.Zip(m.Meta).Select(p => new
                    {
                        path = p.First.Path,
                        sizeBytes = p.First.Size,
                        sha256 = p.First.Sha256,
                        @class = p.First.Class,
                        residency = p.First.Residency,
                        format = p.Second?.Format,
                        safety = p.Second?.Safety,
                        ollamaRollback = m.RollbackShas.Contains(p.First.Sha256),
                    }),
                }),
            });
            return Exit.Ok;
        }
        foreach (var m in models)
        {
            Console.WriteLine($"{CliOutput.Gb(m.Size),9}  {string.Join("+", m.Formats),-18} {string.Join(",", m.Precisions),-10} "
                + $"{CliOutput.Params(m.Parameters),8}  {Loc.T("safety." + Key(m.Safety)),-22} {CliOutput.Clean(m.Name)}");
        }
        Console.WriteLine(Loc.T("cli.models_total", models.Count, CliOutput.Gb(models.Sum(m => m.Size))));
        var rollback = models.SelectMany(m => m.RollbackFiles).Where(f => f.Residency == Residency.Local).ToList();
        if (rollback.Count > 0)
        {
            Console.WriteLine(Loc.T("cli.rollback_total", rollback.Count, CliOutput.Gb(rollback.Sum(f => f.Size))));
        }
        return Exit.Ok;
    }

    /// <summary>Checks files or folders anywhere (no ledger needed); exit code = worst verdict.</summary>
    public static int Check(IEnumerable<string> targets, bool json)
    {
        var files = new List<string>();
        foreach (var t in targets.Select(Path.GetFullPath))
        {
            if (Directory.Exists(t))
            {
                var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                files.AddRange(Directory.EnumerateFiles(t, "*", opts).Where(f => CheckExt.Contains(Path.GetExtension(f))
                    || Path.GetFileName(f).StartsWith("sha256-", StringComparison.OrdinalIgnoreCase)));
            }
            else if (File.Exists(t))
            {
                files.Add(t);
            }
            else
            {
                Console.Error.WriteLine(Loc.T("cli.not_found", CliOutput.Clean(t)));
                return Exit.Usage;
            }
        }
        var results = files.Select(f => (Path: f, Meta: ModelInspector.Inspect(f))).ToList();
        var worst = results.Select(r => r.Meta.Safety).DefaultIfEmpty(SafetyVerdict.NotAModel).Max();
        if (json)
        {
            CliOutput.Json("marea.check/1", new
            {
                worst,
                files = results.Select(r => new
                {
                    path = r.Path,
                    format = r.Meta.Format,
                    architecture = r.Meta.Architecture,
                    name = r.Meta.Name,
                    precision = r.Meta.Precision,
                    parameters = r.Meta.Parameters,
                    safety = r.Meta.Safety,
                    findings = r.Meta.Findings,
                }),
            });
        }
        else
        {
            foreach (var (path, m) in results)
            {
                Console.WriteLine($"{Loc.T("safety." + Key(m.Safety)),-22} {m.Format,-12} {m.Precision,-8} {CliOutput.Short(path)}");
                foreach (var f in m.Findings)
                {
                    Console.WriteLine($"    - {CliOutput.Clean(f)}");
                }
            }
            Console.WriteLine(Loc.T("cli.check_help"));
        }
        return worst switch
        {
            SafetyVerdict.Dangerous => Exit.Dangerous,
            SafetyVerdict.Suspicious => Exit.Suspicious,
            _ => Exit.Ok,
        };
    }

    /// <summary>Translation key suffix of a verdict (safety.safe_format, ...).</summary>
    public static string Key(SafetyVerdict v) => v switch
    {
        SafetyVerdict.SafeFormat => "safe_format",
        SafetyVerdict.NoCodeFound => "no_code",
        SafetyVerdict.Suspicious => "suspicious",
        SafetyVerdict.Dangerous => "dangerous",
        _ => "not_a_model",
    };
}
