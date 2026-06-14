using System.Diagnostics;
using System.IO.Compression;

using Deckle.Installer;

namespace Deckle.Installer;

// ── InstallFlow ───────────────────────────────────────────────────────────────
//
// The seven-step install, run top to bottom. The steps tick off as real work
// completes — no phantom bar; the only progress bar is the download's, driven by
// actual bytes. The download is the heavy step and shows it.
//
// What this does NOT do: provision the whisper.cpp native runtime or the speech
// models. Those are the app's first-run wizard's job (auto-download, per user).
// The installer's contract is narrow: place the app, integrate it, launch it.
internal static class InstallFlow
{
    private const int TotalSteps = 7;

    public static async Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        ConsoleUi.Banner("Deckle Installer", "Installs Deckle on this PC — no admin required.");

        // ── 1. System check + folder choices ─────────────────────────────────────
        ConsoleUi.Step(1, TotalSteps, "Setup");
        if (!Environment.Is64BitOperatingSystem)
        {
            ConsoleUi.Error("Deckle requires 64-bit Windows.");
            return 1;
        }
        ConsoleUi.Ok($"Windows {Environment.OSVersion.Version} (x64)");

        string installDir = Resolve(cli.InstallDir, cli.AssumeYes, "Install folder (app binaries)", InstallPaths.DefaultInstallDir);
        string dataDir = Resolve(cli.DataDir, cli.AssumeYes, "Data folder (models, settings)", InstallPaths.DefaultDataDir);
        ConsoleUi.Info($"binaries → {installDir}");
        ConsoleUi.Info($"data     → {dataDir}");

        // ── 2. Resolve latest release ────────────────────────────────────────────
        ConsoleUi.Step(2, TotalSteps, "Resolving latest release");
        ReleaseResolver.ResolvedRelease release = await ReleaseResolver.ResolveLatestAsync(ct);
        ConsoleUi.Ok($"Deckle {release.Tag}");

        // ── 3. Download (the heavy step) ──────────────────────────────────────────
        ConsoleUi.Step(3, TotalSteps, "Downloading");
        string tempDir = Path.Combine(Path.GetTempPath(), "Deckle-Installer");
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, $"Deckle-{release.Tag}.zip");

        string expectedSha = ParseSha256Sidecar(await Downloader.GetStringAsync(release.Sha256Url, ct));
        string actualSha = await Downloader.DownloadAsync(release.ZipUrl, zipPath, showProgress: true, ct);

        // ── 4. Verify ─────────────────────────────────────────────────────────────
        ConsoleUi.Step(4, TotalSteps, "Verifying");
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleUi.Error($"Checksum mismatch — expected {expectedSha}, got {actualSha}.");
            TryDelete(zipPath);
            return 1;
        }
        ConsoleUi.Ok("SHA-256 verified");

        // ── 5. Install files ──────────────────────────────────────────────────────
        ConsoleUi.Step(5, TotalSteps, "Installing");
        Directory.CreateDirectory(installDir);
        ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
        TryDelete(zipPath);

        string uninstallerPath = CopySelfAsUninstaller(installDir);
        ConsoleUi.Ok($"files installed ({Mb(DirectorySize(installDir)):0} MB)");

        // ── 6. System integration ─────────────────────────────────────────────────
        ConsoleUi.Step(6, TotalSteps, "Integrating");
        string appExe = Path.Combine(installDir, "Deckle.exe");
        Shortcut.CreateStartMenu(appExe, "Deckle", "Deckle");
        ConsoleUi.Ok("Start Menu shortcut");

        string version = release.Tag.StartsWith('v') ? release.Tag[1..] : release.Tag;
        UninstallEntry.Write(installDir, version, uninstallerPath, DirectorySize(installDir));
        ConsoleUi.Ok("registered in Installed apps");

        // Only touch the environment when the user chose a non-default data folder;
        // otherwise the app's own default stands and we leave no trace behind.
        if (!PathsEqual(dataDir, InstallPaths.DefaultDataDir))
        {
            UserEnvironment.SetDataRoot(dataDir);
            ConsoleUi.Ok($"DECKLE_DATA_ROOT = {dataDir}");
        }

        // ── 7. Launch ─────────────────────────────────────────────────────────────
        ConsoleUi.Step(7, TotalSteps, "Launching");
        Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true, WorkingDirectory = installDir });
        ConsoleUi.Ok("Deckle started");
        ConsoleUi.Info("First launch downloads the speech runtime and a model — follow the setup window.");
        return 0;
    }

    // Prompts for a folder unless a CLI override or --yes short-circuits it.
    private static string Resolve(string? overrideValue, bool assumeYes, string label, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue)) return Path.GetFullPath(overrideValue);
        if (assumeYes) return fallback;
        return Path.GetFullPath(ConsoleUi.Prompt(label, fallback));
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
