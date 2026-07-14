using System.IO;
using System.IO.Compression;
using Deckle.Core;

namespace Deckle.Anytype;

// ── BackendInstallation ──────────────────────────────────────────────────────
//
// Where the headless anytype-cli lives on this machine, the pinned bundle the
// wizard downloads, and the serve spec that runs it. This is the concrete half
// of the BackendProcessSpec seam: everything downstream — the supervisor's
// spawn, the wizard's predicates and install step — asks this class rather
// than re-deriving paths or the pin.
//
// The location follows the frozen layout split (JOURNAL 2026-06-18):
// executables under %LOCALAPPDATA%\Programs\Deckle, user data and credentials
// under %LOCALAPPDATA%\Deckle. The installer's InstallPaths owns the same root
// but is internal to Deckle.Installer; the anytype subfolder is this module's,
// so the module resolves it itself.
public static class BackendInstallation
{
    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Deckle", "anytype");

    public static string ExecutablePath { get; } = Path.Combine(InstallDirectory, "anytype.exe");

    public sealed record BackendBundle(
        string Version,
        string Url,
        string Sha256,
        long SizeBytes,
        string DisplayName);

    // The version pin (known-good, never auto-update — JOURNAL 2026-06-18).
    // The release publishes no checksum manifest, so the SHA-256 is the GitHub
    // per-asset digest pinned by ourselves (JOURNAL 2026-07-01, measured on
    // v0.3.6). Installing the binary is only half the first run: the bot
    // account and API key are a separate, interactive provisioning act.
    public static BackendBundle CurrentBundle { get; } = new(
        Version:     "0.3.6",
        Url:         "https://github.com/anyproto/anytype-cli/releases/download/v0.3.6/anytype-cli-v0.3.6-windows-amd64.zip",
        Sha256:      "3aa8db0a02f9349164c1dacf5ede32e8a0b0cf966ced59cb37ff82e2605ab1be",
        SizeBytes:   46_072_129L,
        DisplayName: "Anytype backend (anytype-cli)");

    // The provisioning predicate: has the pinned binary been downloaded? False
    // means the module stays dormant — never a boot failure.
    public static bool IsInstalled() => File.Exists(ExecutablePath);

    // Extracts a downloaded (and already checksum-verified) bundle zip into the
    // install folder. The asset's internal layout is not contractual, so when
    // the exe lands one folder down, its folder's contents are lifted to the
    // root — ExecutablePath is the contract, not the zip shape. Returns true
    // when the exe is in place at the end.
    public static async Task<bool> InstallFromZipAsync(string zipPath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(InstallDirectory);
            ZipFile.ExtractToDirectory(zipPath, InstallDirectory, overwriteFiles: true);

            if (File.Exists(ExecutablePath)) return true;

            // Nested layout — find the exe and lift its folder's contents up.
            string? nested = Directory
                .EnumerateFiles(InstallDirectory, "anytype.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (nested is null) return false;

            string nestedDir = Path.GetDirectoryName(nested)!;
            foreach (string entry in Directory.EnumerateFileSystemEntries(nestedDir))
            {
                string dest = Path.Combine(InstallDirectory, Path.GetFileName(entry));
                if (Directory.Exists(entry)) Directory.Move(entry, dest);
                else File.Move(entry, dest, overwrite: true);
            }
            Directory.Delete(nestedDir, recursive: true);

            return File.Exists(ExecutablePath);
        }, ct).ConfigureAwait(false);
    }

    public static async Task<ProvisioningResult> ProvisionAsync(
        IProgress<Downloader.DownloadProgress> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(InstallDirectory);
        string zipPath = Path.Combine(InstallDirectory, "_bundle.zip");
        try
        {
            Downloader.DownloadResult download = await Downloader.DownloadAsync(
                CurrentBundle.Url, zipPath, CurrentBundle.Sha256, progress, ct);
            if (!download.Success)
                return ProvisioningResult.Fail(download.ErrorMessage ?? "download failed");

            bool installed = await InstallFromZipAsync(zipPath, ct);
            return installed
                ? ProvisioningResult.Ok(CurrentBundle.SizeBytes, download.ActualSha256)
                : ProvisioningResult.Fail("bundle did not contain anytype.exe");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); }
            catch { /* best-effort cleanup */ }
        }
    }

    // The serve invocation the supervisor spawns. --no-update-check because
    // the version pin is Deckle's (known-good + signal newer, never
    // auto-update — JOURNAL 2026-06-18); the CLI must not self-nag or
    // self-move. No embedded paths, so no quoting concerns in the arguments.
    public static BackendProcessSpec ServeSpec() =>
        new(ExecutablePath, "serve --no-update-check");
}
