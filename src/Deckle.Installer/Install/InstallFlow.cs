using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

using Deckle.Install;

namespace Deckle.Installer;

// The silent stub: resolve the latest GitHub release, download the app payload
// into a unique temp folder, verify its SHA-256, extract it there, then launch the
// WinUI first-run wizard from that folder and exit. Nothing is asked and nothing is
// integrated here — folder choice, module selection, the Start Menu shortcut, the
// Installed-apps entry, DECKLE_DATA_ROOT and the binary copy all belong to the
// wizard. This is the web-installer half of a VS Code / Discord style setup.
//
// The wizard is handed two things it can't otherwise know: --stub, this running
// exe's path, which it copies into the install folder as the registered
// uninstaller; and --cleanup, the temp root, which it deletes once it has read what
// it needs. The temp folder is therefore deliberately NOT cleaned on success — the
// wizard owns it. On any failure the temp folder is removed best-effort and the
// error goes to a message box.
internal static class InstallFlow
{
    // Owns the window and the message loop; the work runs on a background Task that
    // reports through the window and, when done or cancelled, tears it down.
    public static int Run(CliArgs cli)
    {
        var window = new ProgressWindow("Deckle Setup");
        using var cts = new CancellationTokenSource();
        window.Cancelled += cts.Cancel; // title-bar X cancels the token

        int exitCode = 0;
        Task worker = Task.Run(async () =>
        {
            try { exitCode = await RunCoreAsync(window, cts.Token).ConfigureAwait(false); }
            finally { window.RequestClose(); }
        });

        window.Show();
        window.RunMessageLoop();           // blocks on the main thread until torn down
        worker.GetAwaiter().GetResult();   // let cancellation/cleanup finish before exit
        return exitCode;
    }

    private static async Task<int> RunCoreAsync(ProgressWindow window, CancellationToken ct)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            InstallLog.Error("Install refused | reason=os_not_64bit");
            MessageDialog.Error(window.Handle, "Deckle requires 64-bit Windows.");
            return 1;
        }

        // Tracked so every failure path can remove it; left in place only on success,
        // where the wizard takes ownership.
        string? tempDir = null;
        string step = "resolve_release";
        try
        {
            window.ReportMarquee("Finding the latest Deckle release…");
            ReleaseResolver.ResolvedRelease release = await ReleaseResolver.ResolveLatestAsync(ct).ConfigureAwait(false);
            string version = ReleaseResolver.BareVersion(release.Tag);
            InstallLog.Info($"Release resolved | tag={release.Tag} | zip_bytes={release.ZipSize}");

            step = "create_temp";
            tempDir = Path.Combine(Path.GetTempPath(), "Deckle-Setup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, $"Deckle-{release.Tag}.zip");

            step = "fetch_sha256";
            string expectedSha = await ReleaseResolver.GetSha256Async(release, ct).ConfigureAwait(false);

            step = "download";
            window.ReportMarquee($"Downloading Deckle {version}…");
            InstallLog.Info($"Download starting | tag={release.Tag} | path={zipPath}");
            long downloadedBytes = 0;
            var downloadClock = Stopwatch.StartNew();
            string actualSha = await Downloader.DownloadAsync(release.ZipUrl, zipPath, (done, total) =>
            {
                downloadedBytes = done;
                string status = DownloadStatus(version, done, total);
                if (total is > 0) window.ReportProgress(status, done, total.Value);
                else window.ReportMarquee(status); // no Content-Length: keep the bar alive
            }, ct).ConfigureAwait(false);
            InstallLog.Info(
                $"Download completed | tag={release.Tag} | size_bytes={downloadedBytes} | duration_ms={downloadClock.ElapsedMilliseconds}");

            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                InstallLog.Error(
                    $"Install failed | reason=sha256_mismatch | expected={expectedSha} | actual={actualSha}");
                TryDeleteTree(tempDir);
                MessageDialog.Error(window.Handle,
                    "The downloaded file failed its integrity check and was discarded.\n\n" +
                    $"Expected SHA-256:\n{expectedSha}\n\nActual:\n{actualSha}");
                return 1;
            }

            step = "extract";
            window.ReportMarquee($"Extracting Deckle {version}…");
            string payloadDir = Path.Combine(tempDir, "app");
            Directory.CreateDirectory(payloadDir);
            ZipFile.ExtractToDirectory(zipPath, payloadDir, overwriteFiles: true);
            // Extraction itself is synchronous, but a close request received while
            // it runs must still stop the hand-off instead of launching the wizard.
            ct.ThrowIfCancellationRequested();
            TryDelete(zipPath); // dead weight now — the wizard runs from the extracted tree

            string? appExe = FindDeckleExe(payloadDir);
            if (appExe is null)
            {
                InstallLog.Error($"Install failed | reason=exe_missing | payload={payloadDir}");
                TryDeleteTree(tempDir);
                MessageDialog.Error(window.Handle, "The downloaded package did not contain Deckle.exe.");
                return 1;
            }

            ct.ThrowIfCancellationRequested();
            step = "launch_wizard";

            var psi = new ProcessStartInfo(appExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(appExe)!,
            };
            psi.ArgumentList.Add("--install");
            psi.ArgumentList.Add("--stub");
            psi.ArgumentList.Add(Environment.ProcessPath ?? string.Empty);
            psi.ArgumentList.Add("--cleanup");
            psi.ArgumentList.Add(tempDir);
            // With UseShellExecute=false Start either returns the new process or
            // throws, so the pid is always there to record the hand-off.
            using (Process wizard = Process.Start(psi)!)
                InstallLog.Info($"Wizard launched | tag={release.Tag} | pid={wizard.Id} | exe={appExe}");
            return 0; // temp left in place on purpose — the wizard owns it now
        }
        catch (OperationCanceledException)
        {
            InstallLog.MarkCancelled();
            InstallLog.Info($"Install cancelled | reason=user_closed | step={step}");
            if (tempDir is not null) TryDeleteTree(tempDir);
            return 1; // user closed the window; no error dialog
        }
        catch (Exception ex)
        {
            string status = ex is HttpRequestException { StatusCode: { } code } ? $" | status={(int)code}" : string.Empty;
            InstallLog.Error($"Install failed | reason={step}_failed | step={step}{status} | {InstallLog.Cause(ex)}");
            if (tempDir is not null) TryDeleteTree(tempDir);
            MessageDialog.Error(window.Handle, FriendlyError(ex));
            return 1;
        }
    }

    private static string DownloadStatus(string version, long done, long? total)
    {
        static double Mb(long bytes) => bytes / (1024.0 * 1024.0);
        return total is > 0
            ? $"Downloading Deckle {version}…  ({Mb(done):0} MB of {Mb(total.Value):0} MB)"
            : $"Downloading Deckle {version}…  ({Mb(done):0} MB)";
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        HttpRequestException =>
            "Could not reach GitHub to download Deckle.\n\nCheck your internet connection and try again.",
        TimeoutException =>
            "The download stopped receiving data.\n\nCheck your internet connection and try again.",
        _ => "Deckle Setup could not complete:\n\n" + ex.Message,
    };

    // Locates Deckle.exe in the extracted tree — flat at the root, but tolerant
    // of a wrapping folder by taking the shallowest match.
    private static string? FindDeckleExe(string root)
    {
        string direct = Path.Combine(root, "Deckle.exe");
        if (File.Exists(direct)) return direct;

        return Directory.EnumerateFiles(root, "Deckle.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .FirstOrDefault();
    }

    // Best-effort: a leftover file in %TEMP% does not change what the user gets,
    // so a failure is recorded and the flow goes on.
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { InstallLog.Verbose($"temp file delete failed | path={path} | {InstallLog.Cause(ex)}"); }
    }

    private static void TryDeleteTree(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { InstallLog.Verbose($"temp folder delete failed | path={dir} | {InstallLog.Cause(ex)}"); }
    }
}
