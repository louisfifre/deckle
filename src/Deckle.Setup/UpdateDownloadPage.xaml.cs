using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

// ── UpdateDownloadPage ───────────────────────────────────────────────────────
//
// The running app's leg of an update: fetch the new payload into a unique temp
// folder (sidecar SHA-256 verified by the shared Downloader), extract it, and
// hand off to the NEW payload's own `Deckle.exe --update-apply` — the binary
// swap cannot run in this process, whose image is among the files to replace.
// The same two-process split as the install chain, with the roles reversed:
// there the temp process places and the installed process provisions; here the
// installed process fetches and the temp process places.
//
// On success the page completes the window and the App exits, freeing the
// binaries for the --update-apply gate. On failure nothing was touched — the
// temp folder is removed and Retry re-runs the whole fetch.
public sealed partial class UpdateDownloadPage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;

    public UpdateDownloadPage()
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
            Loc.Get("Setup_StepTitle_Update"),
            Loc.Get("Setup_StepSubtitle_Update"));
        setup.SetBackEnabled(false);
        setup.SetNextEnabled(false);
        setup.SetNextVisible(false);
        // The shell Cancel is the abort affordance: Complete(false) closes the
        // window, and the token below aborts the transfer mid-flight.
        setup.SetCancelVisible(true);

        RetryButton.Content = Loc.Get("Setup_Deploy_Retry");

        _cts = new CancellationTokenSource();
        // Window closed (Cancel, X, Alt+F4) with the download in flight — the
        // page never navigates away, so the window teardown is the abort signal.
        setup.Closed += (_, _) => _cts?.Cancel();

        _ = DownloadAsync();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        DownloadProgress.Visibility = Visibility.Visible;
        _ = DownloadAsync();
    }

    // ── Orchestration ─────────────────────────────────────────────────────────

    private async Task DownloadAsync()
    {
        if (_setup is null || _context is null) return;
        if (_context.PendingUpdate is not { } update)
        {
            // A download page without a resolved release is a wiring bug —
            // surfaced as a terminal error rather than a silent no-op window.
            ShowFailed(Loc.Format("Setup_Update_Failed_Format", "no pending update"));
            return;
        }
        var ct = _cts?.Token ?? CancellationToken.None;

        DeckleSetupSource.Log.UpdateDownloadStarted();
        DeckleSetupSource.Log.UpdateDownloadStartedDetail(update.Version, update.ZipUrl, update.ZipSize);

        string? tempDir = null;
        string step = "resolve";
        try
        {
            StatusText.Text = Loc.Get("Setup_Update_Resolving");
            string expectedSha = await ReleaseResolver.GetSha256Async(
                new ReleaseResolver.ResolvedRelease(update.Tag, update.ZipUrl, update.Sha256Url, update.ZipSize), ct);

            tempDir = Path.Combine(Path.GetTempPath(), "Deckle-Update-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, $"Deckle-{update.Tag}.zip");

            step = "download";
            var progress = new Progress<Downloader.DownloadProgress>(OnDownloadProgress);
            var dl = await Downloader.DownloadAsync(update.ZipUrl, zipPath, expectedSha, progress, ct);
            if (!dl.Success)
            {
                TryDeleteTree(tempDir);
                ShowFailed(Loc.Format("Setup_Update_Failed_Format", dl.ErrorMessage ?? "download failed"));
                return;
            }

            step = "extract";
            SetMarquee(Loc.Get("Setup_Update_Extracting"));
            string payloadDir = Path.Combine(tempDir, "app");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(payloadDir);
                ZipFile.ExtractToDirectory(zipPath, payloadDir, overwriteFiles: true);
                File.Delete(zipPath); // dead weight — the apply leg runs from the tree
            }, ct);

            string? newExe = FindDeckleExe(payloadDir);
            if (newExe is null)
            {
                TryDeleteTree(tempDir);
                ShowFailed(Loc.Format("Setup_Update_Failed_Format", Loc.Get("Setup_Update_PayloadMissingExe")));
                return;
            }

            step = "handoff";
            SetMarquee(Loc.Get("Setup_Update_HandingOff"));
            _context.CleanupDirectory = tempDir;
            LaunchUpdateApply(newExe, _context.InstallDirectory, tempDir);

            DeckleSetupSource.Log.UpdateHandoff();
            DeckleSetupSource.Log.UpdateHandoffDetail(newExe, tempDir);

            // The App awaits this completion and exits, releasing the binaries
            // the spawned --update-apply process is waiting to replace.
            _setup.Complete(true);
        }
        catch (OperationCanceledException)
        {
            if (tempDir is not null) TryDeleteTree(tempDir);
            // Cancelled through the window teardown — nothing left to show.
        }
        catch (Exception ex)
        {
            if (tempDir is not null) TryDeleteTree(tempDir);
            DeckleSetupSource.Log.UpdateDownloadFailed();
            DeckleSetupSource.Log.UpdateDownloadFailedDetail(step, $"{ex.GetType().Name}: {ex.Message}");
            ShowFailed(Loc.Format("Setup_Update_Failed_Format", ex.Message));
        }
    }

    private static void LaunchUpdateApply(string newExe, string installDir, string tempDir)
    {
        var psi = new ProcessStartInfo(newExe)
        {
            UseShellExecute  = false,
            WorkingDirectory = Path.GetDirectoryName(newExe)!,
        };
        psi.ArgumentList.Add("--update-apply");
        psi.ArgumentList.Add("--target");
        psi.ArgumentList.Add(installDir);
        psi.ArgumentList.Add("--cleanup");
        psi.ArgumentList.Add(tempDir);
        Process.Start(psi);
    }

    // Locates Deckle.exe in the extracted tree — flat at the root for today's
    // zip, tolerant of a single wrapping folder (same probe as the stub).
    private static string? FindDeckleExe(string root)
    {
        string direct = Path.Combine(root, "Deckle.exe");
        if (File.Exists(direct)) return direct;

        return Directory.EnumerateFiles(root, "Deckle.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .FirstOrDefault();
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void OnDownloadProgress(Downloader.DownloadProgress p)
    {
        if (_dispatcher is null) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (p.Percent is double pct)
            {
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Minimum = 0;
                DownloadProgress.Maximum = 1;
                DownloadProgress.Value   = pct;
                StatusText.Text = Loc.Format(
                    "Setup_Install_Progress_WithTotal_Format",
                    FormatBytes(p.BytesDownloaded),
                    FormatBytes(p.TotalBytes ?? 0),
                    pct.ToString("P0", CultureInfo.CurrentCulture));
            }
            else
            {
                DownloadProgress.IsIndeterminate = true;
                StatusText.Text = Loc.Format(
                    "Setup_Install_Progress_NoTotal_Format", FormatBytes(p.BytesDownloaded));
            }
        });
    }

    private void SetMarquee(string status)
    {
        DownloadProgress.IsIndeterminate = true;
        StatusText.Text = status;
    }

    private void ShowFailed(string message)
    {
        DownloadProgress.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private static void TryDeleteTree(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
