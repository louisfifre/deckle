using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace Deckle.Installer;

// ── InstallFlow ───────────────────────────────────────────────────────────────
//
// Recap → one keystroke → unattended run. Everything is resolved before anything
// is asked (system check, latest release, folder defaults), so the consent screen
// states exactly what will happen — version, download size, both folders. Enter
// is the whole fast path; C reopens the folders; after consent the machine runs
// without questions. The only progress bar is the download's, driven by actual
// bytes, and ticks appear as real work completes — no phantom progress.
//
// What this does NOT do: provision the whisper.cpp native runtime or the speech
// models. Those are the app's first-run wizard's job (auto-download, per user).
// The installer's contract is narrow: place the app, integrate it, launch it.
internal static class InstallFlow
{
    public static async Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        ConsoleUi.Banner("Installer");

        // ── Resolve everything before asking anything ─────────────────────────────
        if (!Environment.Is64BitOperatingSystem)
        {
            ConsoleUi.Error("Deckle requires 64-bit Windows.");
            return 1;
        }

        ReleaseResolver.ResolvedRelease release;
        try
        {
            release = await ReleaseResolver.ResolveLatestAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            ConsoleUi.Error($"Could not reach GitHub to find the latest release — check your connection. ({ex.Message})");
            return 1;
        }

        string installDir = Path.GetFullPath(cli.InstallDir ?? InstallPaths.DefaultInstallDir);
        string dataDir = Path.GetFullPath(cli.DataDir ?? InstallPaths.DefaultDataDir);

        // ── Recap + single-keystroke consent ──────────────────────────────────────
        // The recap re-prints after each folder edit, so what Enter commits to is
        // always the block on screen.
        bool interactive = !cli.AssumeYes && !Console.IsInputRedirected;
        while (true)
        {
            Recap(release, installDir, dataDir);
            if (!interactive) break;
            ConsoleUi.Hint("Enter installs · C changes the folders · Ctrl+C cancels");
            if (ConsoleUi.WaitKey(ConsoleKey.Enter, ConsoleKey.C) == ConsoleKey.Enter) break;
            Console.WriteLine();
            installDir = Path.GetFullPath(ConsoleUi.PromptPath("App folder", installDir));
            dataDir = Path.GetFullPath(ConsoleUi.PromptPath("Data folder", dataDir));
            Console.WriteLine();
        }

        // ── Unattended run ────────────────────────────────────────────────────────
        ConsoleUi.Phase("Installing");
        string tempDir = Path.Combine(Path.GetTempPath(), "Deckle-Installer");
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, $"Deckle-{release.Tag}.zip");

        string expectedSha = ParseSha256Sidecar(await Downloader.GetStringAsync(release.Sha256Url, ct));
        string actualSha = await Downloader.DownloadAsync(release.ZipUrl, zipPath, showProgress: true, ct);
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleUi.Error($"Checksum mismatch — expected {expectedSha}, got {actualSha}.");
            TryDelete(zipPath);
            return 1;
        }

        Directory.CreateDirectory(installDir);
        ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
        TryDelete(zipPath);
        string uninstallerPath = CopySelfAsUninstaller(installDir);
        ConsoleUi.Ok($"verified (SHA-256) and unpacked — {Mb(DirectorySize(installDir)):0} MB");

        string appExe = Path.Combine(installDir, "Deckle.exe");
        Shortcut.CreateStartMenu(appExe, "Deckle", "Deckle");
        string version = release.Tag.StartsWith('v') ? release.Tag[1..] : release.Tag;
        UninstallEntry.Write(installDir, version, uninstallerPath, DirectorySize(installDir));
        ConsoleUi.Ok("Start Menu · Installed apps");

        // Only touch the environment when the user chose a non-default data folder;
        // otherwise the app's own default stands and we leave no trace behind.
        if (!PathsEqual(dataDir, InstallPaths.DefaultDataDir))
        {
            UserEnvironment.SetDataRoot(dataDir);
            ConsoleUi.Ok($"DECKLE_DATA_ROOT = {dataDir}");
        }

        Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true, WorkingDirectory = installDir });
        Console.WriteLine();
        ConsoleUi.Success("Deckle is installed and running.");
        ConsoleUi.Info("First launch downloads the speech runtime and a model — follow the setup window.");
        return 0;
    }

    // The consent screen: what will install, how heavy the download is, where the
    // two folders land, and why the data folder is the one worth moving.
    private static void Recap(ReleaseResolver.ResolvedRelease release, string installDir, string dataDir)
    {
        string terms = release.ZipSize > 0
            ? $"{Mb(release.ZipSize):0} MB download, no admin"
            : "no admin";
        ConsoleUi.Headline($"Deckle {release.Tag} — ready to install ({terms})");
        Console.WriteLine();
        ConsoleUi.Row("App", installDir);
        ConsoleUi.Row("Data", dataDir);
        ConsoleUi.RowNote("speech models live here and can reach ~3 GB");
        Console.WriteLine();
    }

    // The .sha256 sidecar is `<hex> *<filename>` (sha256sum -c format). Take the hex.
    private static string ParseSha256Sidecar(string content)
    {
        string first = content.Trim().Split(' ', '\t', '\n', '\r')[0];
        return first.ToLowerInvariant();
    }

    // Copies the running installer into the install folder as the uninstaller. When
    // the installer is re-run from inside the folder, source and dest coincide —
    // skip the copy rather than fault on a same-file copy.
    private static string CopySelfAsUninstaller(string installDir)
    {
        string self = Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot resolve the installer's own path.");
        string dest = Path.Combine(installDir, "Deckle-Installer.exe");
        if (!PathsEqual(self, dest)) File.Copy(self, dest, overwrite: true);
        return dest;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static long DirectorySize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static double Mb(long bytes) => bytes / (1024.0 * 1024.0);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
