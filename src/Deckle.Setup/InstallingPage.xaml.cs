using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Setup;

// ── InstallingPage ───────────────────────────────────────────────────────────
//
// Runs the install plan in bulk (sequential V1) and reports per-item progress.
// The rows are no longer hardcoded: InstallPlan maps the wizard's module
// selection to install items (Dictation → native runtime + model + VAD model,
// Autocorrect → CamemBERT, Anytype → the pinned CLI), and this page renders
// one row per item and runs them in order.
//
// Already-installed shortcut: a row whose item detects an existing valid
// install renders as "already installed" without consuming bandwidth.
//
// Errors don't abort the run — each item's result is appended independently so
// the user sees what worked and what didn't, and SummaryPage offers Retry.
//
// Cancellation is handled inline (Cancel install button on the page), not via
// the shell footer. The shell's Cancel button is hidden while this page is
// active to avoid two competing affordances.
public sealed partial class InstallingPage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;

    private sealed record ItemRow(FontIcon Icon, ProgressBar Bar, TextBlock Status);

    public InstallingPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup       = setup;
        _context     = setup.Context;
        _dispatcher  = DispatcherQueue.GetForCurrentThread();

        setup.SetStepHeader(
            Loc.Get("Setup_StepTitle_Installing"),
            Loc.Get("Setup_StepSubtitle_Installing"));
        setup.SetBackEnabled(false);
        setup.SetNextEnabled(false);
        setup.SetNextVisible(false);
        setup.SetCancelVisible(false); // inline Cancel install button instead

        _ = InstallAllAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void OnCancelDownloadClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelDownloadButton.IsEnabled = false;
    }

    // ── Orchestration ─────────────────────────────────────────────────────────

    private async Task InstallAllAsync()
    {
        if (_setup is null || _context is null) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IReadOnlyList<InstallItem> plan = InstallPlan.Build(_context);
        IReadOnlyList<ItemRow> rows = BuildItemRows(plan);
        GlobalProgress.Maximum = plan.Count;

        // A re-entry (SummaryPage's Retry) starts a fresh result set — stale
        // failures must not survive a run that may now succeed.
        _context.Results.Clear();

        for (int i = 0; i < plan.Count; i++)
        {
            InstallItem item = plan[i];
            ItemRow row = rows[i];

            UpdateGlobalStep(i, Loc.Format("Setup_Install_StepOfTotal_Format",
                (i + 1).ToString(CultureInfo.CurrentCulture),
                plan.Count.ToString(CultureInfo.CurrentCulture),
                item.DisplayName));

            if (item.IsInstalled())
            {
                _context.Results.Add(new InstallResult(item.Id, item.DisplayName, true, null, null));
                SetItemDone(row, Loc.Get("Setup_Install_AlreadyInstalled"));
                continue;
            }

            SetItemRunning(row, Loc.Get("Setup_Install_Connecting"));
            var progress = new Progress<Downloader.DownloadProgress>(p => OnDownloadProgress(p, row));

            long startTicks = Environment.TickCount64;
            InstallItemOutcome outcome;
            try
            {
                outcome = await item.RunAsync(progress, ct);
            }
            catch (OperationCanceledException)
            {
                outcome = InstallItemOutcome.Fail("cancelled");
            }
            catch (Exception ex)
            {
                // An item must never take the run down — the failure lands in
                // its row and the next item still gets its chance.
                outcome = InstallItemOutcome.Fail($"{ex.GetType().Name}: {ex.Message}");
            }

            _context.Results.Add(new InstallResult(
                item.Id, item.DisplayName, outcome.Success, outcome.ErrorMessage, outcome.Bytes));

            if (outcome.Success)
            {
                DeckleSetupSource.Log.ItemInstalled();
                DeckleSetupSource.Log.ItemInstalledDetail(
                    item.Id, outcome.Bytes ?? 0, Environment.TickCount64 - startTicks, outcome.Sha256 ?? "");
                SetItemDone(row, outcome.Bytes is { } b
                    ? Loc.Format("Setup_Install_Done_Format", FormatBytes(b))
                    : Loc.Get("Setup_Install_Done"));
            }
            else if (outcome.ErrorMessage == "cancelled")
            {
                DeckleSetupSource.Log.ItemCancelled();
                DeckleSetupSource.Log.ItemCancelledDetail(item.Id);
                SetItemFailed(row, Loc.Get("Setup_Install_Cancelled"));
            }
            else
            {
                DeckleSetupSource.Log.ItemDownloadFailed();
                DeckleSetupSource.Log.ItemDownloadFailedDetail(item.Id, outcome.ErrorMessage ?? "");
                SetItemFailed(row, outcome.ErrorMessage ?? Loc.Get("Setup_Install_UnknownError"));
            }
        }

        UpdateGlobalStep(plan.Count, Loc.Get("Setup_Install_Done"));

        // Hand off to the summary page. Frame.Navigate is UI-thread-safe when
        // invoked from an awaited continuation that resumed on the UI thread.
        _setup.Body.Navigate(typeof(SummaryPage), _setup);
    }

    // ── Rows ──────────────────────────────────────────────────────────────────

    // One Grid per item: glyph column + (label / progress / status) rows —
    // the same shape the former static rows had.
    private IReadOnlyList<ItemRow> BuildItemRows(IReadOnlyList<InstallItem> plan)
    {
        ItemsPanel.Children.Clear();
        var rows = new List<ItemRow>(plan.Count);

        foreach (InstallItem item in plan)
        {
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = Glyphs.Download,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            Grid.SetColumn(icon, 0);
            Grid.SetRow(icon, 0);
            grid.Children.Add(icon);

            var label = new TextBlock
            {
                Text = item.DisplayName,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            };
            Grid.SetColumn(label, 1);
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var bar = new ProgressBar();
            Grid.SetColumn(bar, 1);
            Grid.SetRow(bar, 1);
            grid.Children.Add(bar);

            var status = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            Grid.SetColumn(status, 1);
            Grid.SetRow(status, 2);
            grid.Children.Add(status);

            ItemsPanel.Children.Add(grid);
            rows.Add(new ItemRow(icon, bar, status));
        }

        return rows;
    }

    // ── UI helpers (must run on UI thread) ────────────────────────────────────

    private void OnDownloadProgress(Downloader.DownloadProgress p, ItemRow row)
    {
        if (_dispatcher is null) return;

        _dispatcher.TryEnqueue(() =>
        {
            if (p.Percent is double pct)
            {
                row.Bar.IsIndeterminate = false;
                row.Bar.Minimum = 0;
                row.Bar.Maximum = 1;
                row.Bar.Value   = pct;
                row.Status.Text = Loc.Format(
                    "Setup_Install_Progress_WithTotal_Format",
                    FormatBytes(p.BytesDownloaded),
                    FormatBytes(p.TotalBytes ?? 0),
                    pct.ToString("P0", CultureInfo.CurrentCulture));
            }
            else
            {
                row.Bar.IsIndeterminate = true;
                row.Status.Text = Loc.Format("Setup_Install_Progress_NoTotal_Format", FormatBytes(p.BytesDownloaded));
            }
        });
    }

    private void UpdateGlobalStep(int completedTasks, string status)
    {
        GlobalProgress.Value  = completedTasks;
        GlobalStatusText.Text = status;
    }

    private static void SetItemRunning(ItemRow row, string text)
    {
        row.Icon.Glyph = Glyphs.Download;
        row.Bar.Visibility = Visibility.Visible;
        row.Bar.IsIndeterminate = true;
        row.Status.Text = text;
    }

    private static void SetItemDone(ItemRow row, string text)
    {
        row.Icon.Glyph = Glyphs.Badge.Success;
        row.Bar.IsIndeterminate = false;
        row.Bar.Maximum = 1;
        row.Bar.Value = 1;
        row.Status.Text = text;
    }

    private static void SetItemFailed(ItemRow row, string text)
    {
        row.Icon.Glyph = Glyphs.Badge.Critical;
        row.Bar.IsIndeterminate = false;
        row.Bar.Value = 0;
        row.Status.Text = text;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
