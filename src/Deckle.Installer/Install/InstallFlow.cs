using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace Deckle.Installer;

// ── InstallFlow ───────────────────────────────────────────────────────────────
//
// The silent stub: resolve the latest GitHub release, download the app payload
// into a unique temp folder, verify its SHA-256, extract it there, then launch the
// WinUI first-run wizard from that folder and exit. Nothing is asked and nothing is
// integrated here — folder choice, module selection, the Start Menu shortcut, the
// Installed-apps entry, DECKLE_DATA_ROOT and the binary copy all moved into the
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
            MessageDialog.Error(window.Handle, "Deckle requires 64-bit Windows.");
            return 1;
        }

        // Tracked so every failure path can remove it; left in place only on success,
        // where the wizard takes ownership.
        string? tempDir = null;
        try
        {
            window.ReportMarquee("Finding the latest Deckle release…");
            ReleaseResolver.ResolvedRelease release = await ReleaseResolver.ResolveLatestAsync(ct).ConfigureAwait(false);
            string version = BareVersion(release.Tag);

            tempDir = Path.Combine(Path.GetTempPath(), "Deckle-Setup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, $"Deckle-{release.Tag}.zip");

            string expectedSha = ParseSha256Sidecar(await Downloader.GetStringAsync(release.Sha256Url, ct).ConfigureAwait(false));

            window.ReportMarquee($"Downloading Deckle {version}…");
            string actualSha = await Downloader.DownloadAsync(release.ZipUrl, zipPath, (done, total) =>
            {
                string status = DownloadStatus(version, done, total);
                if (total is > 0) window.ReportProgress(status, done, total.Value);
                else window.ReportMarquee(status); // no Content-Length: keep the bar alive
            }, ct).ConfigureAwait(false);

            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteTree(tempDir);
                MessageDialog.Error(window.Handle,
                    "The downloaded file failed its integrity check and was discarded.\n\n" +
                    $"Expected SHA-256:\n{expectedSha}\n\nActual:\n{actualSha}");
                return 1;
            }

            window.ReportMarquee($"Extracting Deckle {version}…");
            string payloadDir = Path.Combine(tempDir, "app");
            Directory.CreateDirectory(payloadDir);
            ZipFile.ExtractToDirectory(zipPath, payloadDir, overwriteFiles: true);
            TryDelete(zipPath); // dead weight now — the wizard runs from the extracted tree

            string? appExe = FindDeckleExe(payloadDir);
            if (appExe is null)
            {
                TryDeleteTree(tempDir);
                MessageDialog.Error(window.Handle, "The downloaded package did not contain Deckle.exe.");
                return 1;
            }

            // Hand off to the wizard living inside the payload. It runs from the
            // extracted folder; --stub lets it register this exe as the uninstaller,
            // and --cleanup lets it delete the whole temp root when it is finished.
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
            Process.Start(psi);
            return 0; // temp left in place on purpose — the wizard owns it now
        }
        catch (OperationCanceledException)
        {
            if (tempDir is not null) TryDeleteTree(tempDir);
            return 1; // user closed the window; no error dialog
        }
        catch (Exception ex)
        {
            if (tempDir is not null) TryDeleteTree(tempDir);
            MessageDialog.Error(window.Handle, FriendlyError(ex));
            return 1;
        }
    }

    // The status line under the bar — "Downloading Deckle 0.4.0…  (52 MB of 300 MB)".
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
        _ => "Deckle Setup could not complete:\n\n" + ex.Message,
    };

    // Locates Deckle.exe in the extracted tree — flat at the root for today's zip,
    // but tolerant of a single wrapping folder by taking the shallowest match.
    private static string? FindDeckleExe(string root)
    {
        string direct = Path.Combine(root, "Deckle.exe");
        if (File.Exists(direct)) return direct;

        return Directory.EnumerateFiles(root, "Deckle.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .FirstOrDefault();
    }

    // "v0.7.1" → "0.7.1" — the tag with its leading v dropped, as the wizard and the
    // status text want it.
    private static string BareVersion(string tag) => tag.StartsWith('v') ? tag[1..] : tag;

    // The .sha256 sidecar is `<hex> *<filename>` (sha256sum -c format). Take the hex.
    private static string ParseSha256Sidecar(string content)
    {
        string first = content.Trim().Split(' ', '\t', '\n', '\r')[0];
        return first.ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static void TryDeleteTree(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
