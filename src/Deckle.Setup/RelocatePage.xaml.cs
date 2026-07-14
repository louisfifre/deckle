using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Install;

namespace Deckle.Setup;

// ── RelocatePage ─────────────────────────────────────────────────────────────
//
// Moves the data root as a transaction: recheck the target drive's space,
// copy the whole tree with byte progress, flip DECKLE_DATA_ROOT (cleared when
// the target is the built-in default), then relaunch the plain app pointed at
// the new root with `--cleanup-data <old root>` — the origin is removed only
// by the relaunched process, once nothing holds it. Order matters: prepare
// beside, then switch, then clean — the same posture as the install and
// update chains, never a move under the app's own feet.
//
// Runs in the dedicated `--relocate-data` process (App.Relocate.cs): the
// normal app cannot copy its own live root (sinks and engines hold files
// open), so it exits and this process — whose only open handles under the
// source are its log sinks — does the work. Files that still refuse to copy
// (in practice the diagnostics this very process is writing) are skipped and
// counted rather than failing the move: logs are shed, user data is not.
public sealed partial class RelocatePage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;

    public RelocatePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup      = setup;
        _context    = setup.Context;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        setup.SetStepHeader(
            Loc.Get("Setup_StepTitle_Relocate"),
            Loc.Get("Setup_StepSubtitle_Relocate"));
        setup.SetBackEnabled(false);
        setup.SetNextEnabled(false);
        setup.SetNextVisible(false);
        // The shell Cancel aborts the copy; the partial target is removed and
        // the old root stays the live one (the App relaunches plain on false).
        setup.SetCancelVisible(true);

        RetryButton.Content = Loc.Get("Setup_Deploy_Retry");

        _cts = new CancellationTokenSource();
        setup.Closed += (_, _) => _cts?.Cancel();

        _ = RelocateAsync();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        RelocateProgress.Visibility = Visibility.Visible;
        _ = RelocateAsync();
    }

    // ── Orchestration ─────────────────────────────────────────────────────────

    private async Task RelocateAsync()
    {
        if (_setup is null || _context is null) return;
        var ct = _cts?.Token ?? CancellationToken.None;

        string source = Path.GetFullPath(AppPaths.UserDataRoot);
        string target = Path.GetFullPath(_context.DataDirectory);

        string step = "check";
        bool committed = false;
        try
        {
            SetMarquee(Loc.Get("Setup_Relocate_Checking"));
            long required = await Task.Run(() => DataRootTree.MeasureBytes(source), ct);

            DeckleSetupSource.Log.RelocateStarted();
            DeckleSetupSource.Log.RelocateStartedDetail(source, target, required);

            // The authoritative space gate — the Settings pre-check ran in
            // another process, minutes may have passed.
            var drive = new DriveInfo(Path.GetPathRoot(target)!);
            if (drive.AvailableFreeSpace < required)
            {
                Fail(step,
                    $"insufficient_space drive={drive.Name} required={required} free={drive.AvailableFreeSpace}",
                    Loc.Format("Setup_Relocate_InsufficientSpace_Format",
                        drive.Name, FormatBytes(required), FormatBytes(drive.AvailableFreeSpace)));
                return;
            }

            // The app that spawned us is exiting right now, its sinks still
            // flushing into the source — absorb that latency so the copy never
            // snapshots files mid-write. Another Deckle still alive after the
            // window is a genuine block, surfaced retryable.
            step = "gate";
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath!)!;
            string[] running = RunningProcesses.FromFolder(exeDir);
            for (int i = 0; i < 30 && running.Length > 0; i++)
            {
                await Task.Delay(500, ct);
                running = await Task.Run(() => RunningProcesses.FromFolder(exeDir), ct);
            }
            if (running.Length > 0)
            {
                Fail(step, "app_still_running", Loc.Get("Setup_Deploy_AppRunning"));
                return;
            }

            step = "copy";
            long startTicks = Environment.TickCount64;
            var relocator = new DataRootRelocator(
                new DataRootTree(),
                new UserDataRootSelection(),
                new DeckleDataRootLauncher());
            var progress = new Progress<DataRootCopyProgress>(
                p => OnCopyProgress(p.CopiedBytes, p.TotalBytes));

            step = "relocate";
            DataRootCopyResult result = await Task.Run(
                () => relocator.Relocate(source, target, required, progress, ct), ct);
            committed = true;

            DeckleSetupSource.Log.RelocateCompleted();
            DeckleSetupSource.Log.RelocateCompletedDetail(
                result.CopiedBytes, result.Files, result.SkippedFiles,
                Environment.TickCount64 - startTicks);

            _setup.Complete(true);
        }
        catch (OperationCanceledException)
        {
            // DataRootRelocator already rolled back every pre-handoff change.
        }
        catch (Exception ex)
        {
            // The child owns the target after a successful handoff. A local UI
            // or logging failure must not report the committed move as failed.
            if (committed) return;
            Fail(step, $"{ex.GetType().Name}: {ex.Message}", ex.Message);
        }
    }

    // One funnel for every blocking state: the warning narrative and the
    // retryable InfoBar. `display` defaults to the raw reason.
    private void Fail(string step, string reason, string? display = null)
    {
        DeckleSetupSource.Log.RelocateFailed();
        DeckleSetupSource.Log.RelocateFailedDetail(step, reason);
        RelocateProgress.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
        ErrorBar.Message = Loc.Format("Setup_Relocate_Failed_Format", display ?? reason);
        ErrorBar.IsOpen = true;
    }

    // ── Steps (background thread) ─────────────────────────────────────────────

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void OnCopyProgress(long copied, long total)
    {
        if (_dispatcher is null) return;
        _dispatcher.TryEnqueue(() =>
        {
            RelocateProgress.IsIndeterminate = false;
            RelocateProgress.Minimum = 0;
            RelocateProgress.Maximum = 1;
            RelocateProgress.Value = total > 0 ? (double)copied / total : 1;
            StatusText.Text = Loc.Format(
                "Setup_Install_Progress_WithTotal_Format",
                FormatBytes(copied),
                FormatBytes(total),
                (total > 0 ? (double)copied / total : 1).ToString("P0", CultureInfo.CurrentCulture));
        });
    }

    private void SetMarquee(string status)
    {
        RelocateProgress.IsIndeterminate = true;
        StatusText.Text = status;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
