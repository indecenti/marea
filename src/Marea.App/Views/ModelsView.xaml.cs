using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Every model in the managed folders, read from its own header: format, precision,
/// parameters, size, residency and whether loading it could run code.</summary>
public partial class ModelsView : UserControl, IRefreshable
{
    private sealed record Row(ModelEntry Model, string Name, string Kind, Brush KindBrush, string Format, string Precision,
        string Params, long ParamCount, string Size, long Bytes, string State, string Safety, int SafetyRank,
        Geometry SafetyIcon, Brush SafetyBrush, string Source, int SourceRank, Geometry SourceIcon, Brush SourceBrush,
        WatchFinding? Risk);

    private Dictionary<string, WatchFinding> _risks = new(StringComparer.OrdinalIgnoreCase);

    private bool _ready;

    /// <summary>Creates and loads the page.</summary>
    public ModelsView()
    {
        InitializeComponent();
        Filter.ItemsSource = new[] { "all", "unused", "attention", "at_risk", "gguf", "safetensors", "pickle", "evaporated", "rollback" }
            .Select(k => Loc.T("models.filter_" + k)).ToList();
        _ready = true;
        Filter.SelectedIndex = 0;
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        if (!_ready)
        {
            return;
        }
        var all = App.Engine.Models();
        _risks = App.Engine.SourceRisks().ToDictionary(f => f.File.Path, StringComparer.OrdinalIgnoreCase);
        var attention = all.Count(m => m.Safety >= SafetyVerdict.Suspicious);
        AttentionChip.Visibility = attention > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttentionText.Text = Loc.T(attention == 1 ? "models.attention_one" : "models.attention", attention);
        TotalText.Text = Loc.T("models.total", all.Count, Fmt.Gb(all.Sum(m => m.Size)));
        var rollbackBytes = all.SelectMany(m => m.RollbackFiles).Where(f => f.Residency == Residency.Local).Sum(f => f.Size);
        RollbackChip.Visibility = rollbackBytes > 0 && Filter.SelectedIndex != RollbackFilter ? Visibility.Visible : Visibility.Collapsed;
        RollbackText.Text = Loc.T("models.rollback_chip", Fmt.Gb(rollbackBytes));
        System.Windows.Automation.AutomationProperties.SetName(RollbackChip, RollbackText.Text);
        IEnumerable<ModelEntry> models = Filter.SelectedIndex switch
        {
            1 => all.Where(m => m.UnusedFor(UnusedDays, DateTime.UtcNow)),
            2 => all.Where(m => m.Safety >= SafetyVerdict.Suspicious),
            3 => all.Where(m => RiskOf(m) is not null),
            4 => all.Where(m => m.Formats.Contains("GGUF")),
            5 => all.Where(m => m.Formats.Contains("safetensors")),
            6 => all.Where(m => m.Formats.Any(f => f is "PyTorch" or "TorchScript" or "pickle")),
            7 => all.Where(m => m.State != ModelResidency.OnDisk),
            RollbackFilter => all.Where(m => m.RollbackFiles.Count > 0),
            _ => all,
        };
        var q = Search.Text.Trim();
        if (q.Length > 0)
        {
            models = models.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                       || (m.Architecture?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                                       || m.Precisions.Any(p => p.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        var rows = models.Select(ToRow).ToList();
        FreeUnusedButton.Visibility = Filter.SelectedIndex == 1 && rows.Any(r => r.Model.State != ModelResidency.Evaporated)
                                      || Filter.SelectedIndex == RollbackFilter && rollbackBytes > 0
            ? Visibility.Visible : Visibility.Collapsed;
        FreeText.Text = Loc.T(Filter.SelectedIndex == RollbackFilter ? "models.free_rollback" : "models.free_unused");
        Grid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnSelection(this, null!);
    }

    private Row ToRow(ModelEntry m)
    {
        var meta = m.Meta.All(x => x is null);
        var kind = Loc.T("kind." + m.Kind.ToString().ToLowerInvariant())
                   + (m.Architecture is { } a ? " · " + a : "") + " · " + Fmt.RelativeFolder(m.Files[0]);
        var risk = RiskOf(m);
        var (source, sourceRank) = risk is { } r
            ? (Loc.T("risk." + r.Action.ToString().ToLowerInvariant()), 10 + (int)r.Action)
            : (SourceName(m.Files[0]), (int)m.Files[0].Class);
        var sourceIcon = (Geometry)FindResource(risk is null ? Fmt.ClassIcon(m.Files[0].Class) : risk.Action == WatchAction.Lost ? "Icon.ShieldError" : "Icon.Warning");
        var sourceBrush = (Brush)FindResource(risk is null ? m.Files[0].Class + "Brush" : risk.Action == WatchAction.Lost ? "DangerBrush" : "WarningBrush");
        return new Row(m, m.Name, kind, (Brush)FindResource(m.Kind == ModelKind.Ollama ? "DerivableBrush" : "SeaAccentBrush"),
            (m.Formats.Count > 0 ? string.Join(" + ", m.Formats) : "—")
                + (m.Precisions.Count > 0 ? " · " + string.Join(", ", m.Precisions) : ""),
            "",
            Fmt.Params(m.Parameters), m.Parameters, Fmt.Gb(m.Size), m.Size,
            Loc.T("mstate." + m.State.ToString().ToLowerInvariant()),
            meta ? Loc.T("safety.pending") : Fmt.Safety(m.Safety), (int)m.Safety,
            (Geometry)FindResource(Fmt.SafetyIcon(m.Safety)), (Brush)FindResource(Fmt.SafetyBrush(m.Safety)),
            source, sourceRank, sourceIcon, sourceBrush, risk);
    }

    /// <summary>The most serious source finding among a model's files (lost, then rescue, then protect).</summary>
    private WatchFinding? RiskOf(ModelEntry m) =>
        m.Files.Select(f => _risks.GetValueOrDefault(f.Path)).OfType<WatchFinding>()
            .OrderBy(f => f.Action == WatchAction.Lost ? 0 : f.Action == WatchAction.Rescue ? 1 : 2).FirstOrDefault();

    /// <summary>Where a file would come back from, in a word.</summary>
    private static string SourceName(FileRecord f) => f.Class switch
    {
        Recoverability.Duplicate => Loc.T("srcname.copy"),
        Recoverability.Derivable => Loc.T("srcname.recipe"),
        Recoverability.Reobtainable when f.SourceLabel.StartsWith("src.mirror", StringComparison.Ordinal) => Loc.T("srcname.mirror"),
        Recoverability.Reobtainable when f.SourceLabel.StartsWith("src.ollama", StringComparison.Ordinal) => "Ollama",
        Recoverability.Reobtainable => "Hugging Face",
        _ => Loc.T("srcname.none"),
    };

    private void OnFilter(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <summary>Shows what the header says, the safety verdict in plain words and the files.</summary>
    private void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not Row row)
        {
            DetailBadge.Background = (Brush)FindResource("SeaAccentBrush");
            DetailIcon.Data = (Geometry)FindResource("Icon.Cube");
            DetailTitle.Text = Loc.T("models.select_title");
            DetailText.Text = Loc.T("models.select_hint");
            DetailFiles.Text = "";
            OpenButton.IsEnabled = false;
            RescueButton.Visibility = ProtectButton.Visibility = Visibility.Collapsed;
            return;
        }
        var m = row.Model;
        DetailBadge.Background = row.SafetyBrush;
        DetailIcon.Data = row.SafetyIcon;
        DetailTitle.Text = $"{m.Name}  ·  {row.Safety}";
        var text = Loc.T("safety_help." + Fmt.SafetyKey(m.Safety));
        if (m.Findings.Count > 0)
        {
            text += "\n" + Loc.T("models.findings") + " " + string.Join(", ", m.Findings.Take(6));
        }
        if (row.Risk is { } risk)
        {
            text = Loc.T("risk_help." + risk.Action.ToString().ToLowerInvariant()) + "\n"
                   + Loc.T("risk.why", Loc.Render(risk.Detail)) + "\n\n" + text;
            DetailBadge.Background = row.SourceBrush;
            DetailIcon.Data = row.SourceIcon;
            DetailTitle.Text = $"{m.Name}  ·  {row.Source}";
        }
        var rollback = m.RollbackFiles.Where(f => f.Residency == Residency.Local).Sum(f => f.Size);
        if (rollback > 0)
        {
            text += "\n\n" + Loc.T("models.rollback_help", Fmt.Gb(rollback));
        }
        DetailText.Text = text;
        RescueButton.Visibility = row.Risk?.Action == WatchAction.Rescue ? Visibility.Visible : Visibility.Collapsed;
        ProtectButton.Visibility = row.Risk?.Action == WatchAction.Protect ? Visibility.Visible : Visibility.Collapsed;
        ProtectButton.IsEnabled = !string.IsNullOrEmpty(App.Engine.Settings.MirrorRoot);
        ProtectButton.ToolTip = ProtectButton.IsEnabled ? null : Loc.T("models.protect_no_mirror");
        DetailFiles.Text = string.Join("\n", m.Files.Zip(m.Meta).Take(8).Select(p =>
            $"{Fmt.Gb(p.First.Size),9}  {p.Second?.Format ?? "?",-12} {Fmt.Residency(p.First.Residency),-12} {Fmt.ShortPath(p.First.Path)}"
            + (m.RollbackShas.Contains(p.First.Sha256) ? "  · " + Loc.T("models.rollback_tag") : "")))
            + (m.Files.Count > 8 ? "\n" + Loc.T("models.more_files", m.Files.Count - 8) : "");
        OpenButton.IsEnabled = System.IO.Directory.Exists(m.Folder);
    }

    /// <summary>Models with no file used for this many days count as unused.</summary>
    private const int UnusedDays = 90;

    /// <summary>Position of the "Ollama rollback copies" filter.</summary>
    private const int RollbackFilter = 8;

    private void OnShowRollback(object sender, RoutedEventArgs e) => Filter.SelectedIndex = RollbackFilter;

    /// <summary>Opens the detailed plan for the files of the unused models still on disk (the gate
    /// still decides, file by file, what may leave).</summary>
    private void OnFreeUnused(object sender, RoutedEventArgs e)
    {
        var files = Grid.Items.Cast<Row>()
            .SelectMany(r => Filter.SelectedIndex == RollbackFilter ? r.Model.RollbackFiles : r.Model.Files)
            .Where(f => f.Residency == Residency.Local).ToList();
        PlanWindow.ShowFor((MainWindow)Window.GetWindow(this), files);
    }

    private async void OnCheckSources(object sender, RoutedEventArgs e)
    {
        WatchOutcome? outcome = null;
        var main = (MainWindow)Window.GetWindow(this);
        await main.RunAsync(Loc.T("op.watch"), async (p, ct) => outcome = await App.Engine.WatchAsync(act: true, p, ct));
        if (outcome is { } o)
        {
            MessageBox.Show(main, Loc.T("models.watch_done", o.Report.SourcesChecked, o.Rescued, o.Remaining.Count), "Marea",
                MessageBoxButton.OK, o.Remaining.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    private async void OnRescue(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Row row)
        {
            return;
        }
        var files = row.Model.Files.Where(f => f.Residency == Residency.Evaporated).ToList();
        string? problem = null;
        var main = (MainWindow)Window.GetWindow(this);
        await main.RunAsync(Loc.T("op.bring_back"), async (p, ct) => problem = await App.Engine.BringBackAsync(files, p, ct));
        if (problem is not null)
        {
            MessageBox.Show(main, problem, "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnProtect(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Row row)
        {
            return;
        }
        var files = row.Model.Files.Where(f => f.Residency == Residency.Local).ToList();
        await ((MainWindow)Window.GetWindow(this)).RunAsync(Loc.T("op.replicate"), async (p, ct) =>
            await App.Engine.ReplicateAsync(files, p, ct));
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is Row row && System.IO.Directory.Exists(row.Model.Folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { row.Model.Folder }, UseShellExecute = false });
        }
    }
}
