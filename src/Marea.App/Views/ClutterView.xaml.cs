using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Regenerable space outside the model folders (package caches, idle temporary files, crash
/// dumps) and space that needs a decision. Measure-only: the page offers the owner tool's command to
/// copy and the folder to open, and never deletes anything itself.</summary>
public partial class ClutterView : UserControl
{
    private const int StaleDays = 7;

    // The walk takes a while on a full disk: keep the last result while the app runs.
    private static List<ClutterItem>? _last;
    private CancellationTokenSource? _cts;

    /// <summary>Creates the page; measures on first use, then shows the last result.</summary>
    public ClutterView()
    {
        InitializeComponent();
        Unloaded += (_, _) => _cts?.Cancel();
        if (_last is null)
        {
            _ = MeasureAsync();
        }
        else
        {
            Show(_last);
        }
    }

    private async void OnMeasure(object sender, RoutedEventArgs e) => await MeasureAsync();

    private async Task MeasureAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        MeasureButton.IsEnabled = false;
        Busy.Visibility = Visibility.Visible;
        Status.Text = Loc.T("clutter.measuring");
        try
        {
            var found = await Task.Run(() => ClutterFinder.Find(ClutterPlaces.Current(), StaleDays, ct: cts.Token), cts.Token);
            _last = found;
            Show(found);
        }
        catch (OperationCanceledException)
        {
            // The page was closed or a new measure started.
        }
        finally
        {
            if (_cts == cts)
            {
                MeasureButton.IsEnabled = true;
                Busy.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Show(List<ClutterItem> items)
    {
        var reclaimable = items.Where(i => i.Reclaimable).ToList();
        var other = items.Where(i => !i.Reclaimable).ToList();
        Reclaimable.ItemsSource = reclaimable.Select(Row).ToList();
        Other.ItemsSource = other.Select(Row).ToList();
        OtherTitle.Visibility = other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = reclaimable.Count == 0
            ? Loc.T("clutter.none")
            : Loc.T("clutter.total", Fmt.Gb(reclaimable.Sum(i => i.FreeableBytes)), reclaimable.Count);
    }

    private object Row(ClutterItem i)
    {
        var advice = ClutterFinder.Advice(i, StaleDays);
        var notes = string.Join("\n", advice.Skip(1).Concat(i.Parts.Take(5).Select(p => $"{Fmt.Gb(p.Bytes)}  {Fmt.HomePath(p.Path)}"))
            .Concat(i.Parts.Count > 5 ? [Loc.T("clutter.more", i.Parts.Count - 5)] : []));
        var (icon, brush) = i.Kind switch
        {
            ClutterKind.PackageCache => ("Icon.Library", "SeaAccentBrush"),
            ClutterKind.StaleTemp => ("Icon.Timer", "SeaAccentBrush"),
            ClutterKind.CrashDumps => ("Icon.Warning", "SeaAccentBrush"),
            ClutterKind.Hibernation => ("Icon.Power", "UniqueBrush"),
            _ => ("Icon.Storage", "UniqueBrush"),
        };
        // Only a cache's command empties it safely as is; the hibernation command changes a system setting.
        var command = i.Kind == ClutterKind.PackageCache || i.Kind == ClutterKind.Hibernation ? i.Command : null;
        return new
        {
            Icon = (Geometry)FindResource(icon),
            Brush = (Brush)FindResource(brush),
            Name = i.Kind == ClutterKind.PackageCache ? $"{Loc.T("clutter.kind.package_cache")} · {i.Id}" : Loc.T("clutter.kind." + ClutterFinder.KindKey(i.Kind)),
            i.Path,
            Display = Fmt.HomePath(i.Path),
            Folder = Directory.Exists(i.Path) ? i.Path : System.IO.Path.GetDirectoryName(i.Path) ?? i.Path,
            Size = Fmt.Gb(i.Reclaimable ? i.FreeableBytes : i.Bytes),
            Advice = advice[0],
            Notes = notes,
            NotesVisibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            Command = command ?? "",
            CopyVisibility = command is null ? Visibility.Collapsed : Visibility.Visible,
        };
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        try
        {
            Clipboard.SetText((string)button.Tag);
            Status.Text = Loc.T("clutter.copied") + ": " + (string)button.Tag;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another program holds the clipboard: the command stays readable on the card.
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var folder = (string)((Button)sender).Tag;
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
        }
    }
}
