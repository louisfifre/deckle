using System;
using System.Diagnostics;
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
        try
        {
            SetMarquee(Loc.Get("Setup_Relocate_Checking"));
            long required = await Task.Run(() => DirectorySizeBytes(source), ct);

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
            var progress = new Progress<(long copied, long total)>(p => OnCopyProgress(p.copied, p.total));
            (long copied, int files, int skipped) =
                await Task.Run(() => CopyTree(source, target, required, progress, ct), ct);

            step = "switch";
            SetMarquee(Loc.Get("Setup_Relocate_Switching"));
            if (PathsEqual(target, InstallPaths.DefaultDataDir)) UserEnvironment.ClearDataRoot();
            else UserEnvironment.SetDataRoot(target);

            step = "relaunch";
            SetMarquee(Loc.Get("Setup_Relocate_Restarting"));
            LaunchOnNewRoot(target, source);

            DeckleSetupSource.Log.RelocateCompleted();
            DeckleSetupSource.Log.RelocateCompletedDetail(
                copied, files, skipped, Environment.TickCount64 - startTicks);

            _setup.Complete(true);
        }
        catch (OperationCanceledException)
        {
            // Cancelled through the window teardown — undo the partial copy;
            // the old root never stopped being the live one.
            TryDeleteTree(target, sparing: source);
        }
        catch (Exception ex)
        {
            TryDeleteTree(target, sparing: source);
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

    internal static long DirectorySizeBytes(string root)
    {
        long total = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; }
            catch { /* a vanished temp file must not fail the estimate */ }
        }
        return total;
    }

    // Copies the tree file by file. A file that refuses to copy after one
    // retry is skipped and counted — in practice only this process's own live
    // log sinks under diagnostics/; user data (models, settings, modules) has
    // no writer left once the normal app exited to spawn us.
    private static (long copied, int files, int skipped) CopyTree(
        string source, string target, long totalBytes,
        IProgress<(long, long)> progress, CancellationToken ct)
    {
        long copied = 0;
        int files = 0, skipped = 0;
        long lastReport = 0;

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            string relative = Path.GetRelativePath(source, file);
            string dest = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            bool ok = TryCopy(file, dest) || TryCopy(file, dest);
            if (!ok) { skipped++; continue; }

            files++;
            try { copied += new FileInfo(dest).Length; } catch { }

            long now = Environment.TickCount64;
            if (now - lastReport >= 200)
            {
                progress.Report((copied, totalBytes));
                lastReport = now;
            }
        }

        progress.Report((copied, totalBytes));
        return (copied, files, skipped);
    }

    private static bool TryCopy(string file, string dest)
    {
        try { File.Copy(file, dest, overwrite: true); return true; }
        catch { return false; }
    }

    private void LaunchOnNewRoot(string target, string oldRoot)
    {
        string exe = Environment.ProcessPath!;
        // UseShellExecute=false so the child's environment block carries the
        // new root immediately — the HKCU write above only reaches processes
        // launched after a WM_SETTINGCHANGE round-trip (same reasoning as
        // DeployPage.LaunchInstalled).
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute  = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        };
        psi.Environment[UserEnvironment.DataRootVariable] = target;
        psi.ArgumentList.Add("--cleanup-data");
        psi.ArgumentList.Add(oldRoot);
        Process.Start(psi);
    }

    // Removes the partial copy after a failure or a cancel. `sparing` is a
    // belt-and-braces guard: never touch the live source, whatever state the
    // context is in.
    private static void TryDeleteTree(string dir, string sparing)
    {
        if (PathsEqual(dir, sparing)) return;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort — a leftover partial copy is visible, not harmful */ }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\'),
            Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

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
