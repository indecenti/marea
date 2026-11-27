using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marea.App.Views;
using Marea.Core;

namespace Marea.App;

/// <summary>Shell: navigation, the current page, and the bar for long operations and returning files.</summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource? _op;

    /// <summary>Creates the window on the overview page.</summary>
    public MainWindow()
    {
        InitializeComponent();
        UpdateProviderBadge();
        Nav.SelectedIndex = 0;
    }

    /// <summary>Navigates to a page by tag (overview, models, files, clutter, folders, journal, settings).</summary>
    public void Navigate(string tag)
    {
        foreach (ListBoxItem item in Nav.Items)
        {
            if ((string)item.Tag == tag)
            {
                Nav.SelectedItem = item;
            }
        }
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem item)
        {
            return;
        }
        Page.Content = (string)item.Tag switch
        {
            "models" => new ModelsView(),
            "files" => new FilesView(),
            "clutter" => new ClutterView(),
            "folders" => new FoldersView(),
            "journal" => new JournalView(),
            "settings" => new SettingsView(),
            _ => new OverviewView(),
        };
    }

    /// <summary>Reloads the current page (after an operation changed the ledger).</summary>
    public void RefreshPage()
    {
        if (Page.Content is IRefreshable r)
        {
            r.Refresh();
        }
        UpdateProviderBadge();
        App.Current.UpdateTrayText();
    }

    private void UpdateProviderBadge()
    {
        var ok = App.Engine.IsProvider;
        ProviderBadge.Background = ok ? (Brush)FindResource("DuplicateBrush") : (Brush)FindResource("WarningBrush");
        ProviderIcon.Data = (Geometry)FindResource(ok ? "Icon.Shield" : "Icon.Warning");
        ProviderText.Text = ok ? Loc.T("prov.active") : Loc.T("prov.elsewhere");
        ProviderDetail.Text = ok ? Loc.T("prov.active_detail") : Loc.T("prov.elsewhere_detail");
    }

    /// <summary>Runs a long operation with progress and cancel; only one at a time.</summary>
    public async Task RunAsync(string title, Func<IProgress<ScanProgress>, CancellationToken, Task> work)
    {
        if (_op is not null)
        {
            MessageBox.Show(this, Loc.T("op.busy"), "Marea");
            return;
        }
        _op = new CancellationTokenSource();
        OpTitle.Text = title;
        OpDetail.Text = "";
        OpProgress.IsIndeterminate = true;
        OpBar.Visibility = Visibility.Visible;
        HydBar.Visibility = Visibility.Collapsed;
        var progress = new Progress<ScanProgress>(p =>
        {
            OpDetail.Text = $"{p.Phase}: {p.Path}";
            OpProgress.IsIndeterminate = p.Total <= 0;
            OpProgress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
        });
        try
        {
            await Task.Run(() => work(progress, _op.Token));
        }
        catch (OperationCanceledException)
        {
            App.Engine.Ledger.Log("j.op_cancelled", title, 0, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or CloudFiles.CloudFilesException or HttpRequestException)
        {
            App.Engine.Ledger.Log("j.op_failed", title, 0, false, e.Message);
            MessageBox.Show(this, e.Message, "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _op.Dispose();
            _op = null;
            OpBar.Visibility = Visibility.Collapsed;
            RefreshPage();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _op?.Cancel();

    /// <summary>Shows a file coming back (hydration) in the bottom bar.</summary>
    public void ShowHydration(HydrationEvent e)
    {
        if (_op is not null)
        {
            return;
        }
        HydBar.Visibility = e.Finished ? Visibility.Collapsed : Visibility.Visible;
        HydText.Text = Loc.T("hyd.bar", System.IO.Path.GetFileName(e.Path), Loc.Render(e.Source), Fmt.Gb(e.Done), Fmt.Gb(e.Total));
        HydProgress.Value = e.Total > 0 ? (double)e.Done / e.Total : 0;
        if (e.Finished)
        {
            RefreshPage();
        }
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps Marea in the tray: the service must stay up for evaporated files.
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>Set to really close the window (e.g. to rebuild it in another language).</summary>
    public bool AllowClose { get; set; }
}

/// <summary>A page that can reload its data.</summary>
public interface IRefreshable
{
    /// <summary>Reloads data from the engine.</summary>
    void Refresh();
}
