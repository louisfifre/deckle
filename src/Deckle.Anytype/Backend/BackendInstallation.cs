using System.IO;
using Deckle.Core;
using Deckle.Install;

namespace Deckle.Anytype;

// ── BackendInstallation ──────────────────────────────────────────────────────
//
// Where the headless anytype-cli lives on this machine, the pinned bundle the
// wizard downloads, and the serve spec that runs it. This is the concrete half
// of the BackendProcessSpec seam: everything downstream — the supervisor's
// spawn, the wizard's predicates and install step — asks this class rather
// than re-deriving paths or the pin.
//
// The location follows the executable/data split but is intentionally not
// nested in Deckle's replaceable payload: providers live under the shared
// per-user Programs provider root, while data and credentials remain under
// %LOCALAPPDATA%\Deckle (or its configured data root).
public static class BackendInstallation
{
    // Provider executables deliberately live beside, not inside, the replaceable
    // Deckle payload. They also stay outside DECKLE_DATA_ROOT: relocating user
    // data must never move a running image.
    public static string ProvidersDirectory { get; } = InstallPaths.DefaultProvidersDir;

    public static string InstallDirectory { get; } = Path.Combine(ProvidersDirectory, "Anytype");

    // Transitional path used by releases up to 0.31.10. Updates preserve this
    // directory only while its executable is still running; reconciliation may
    // adopt that PID, while every new spawn resolves the activated external copy.
    public static string LegacyInstallDirectory { get; } = InstallPaths.LegacyAnytypeProviderDir;

    private static BackendProviderStore Store { get; } =
        new(InstallDirectory, LegacyInstallDirectory);

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
    public static bool IsInstalled() => Store.IsInstalled();

    // Extracts a downloaded (and already checksum-verified) bundle zip into the
    // install folder. The asset's internal layout is not contractual, so when
    // the exe lands one folder down, its folder's contents are lifted to the
    // root — ExecutablePath is the contract, not the zip shape. Returns true
    // when the exe is in place at the end.
    public static Task<bool> InstallFromZipAsync(string zipPath, CancellationToken ct) =>
        Store.InstallFromZipAsync(zipPath, CurrentBundle.Version, ct);

    // Copies the already verified legacy payload into an immutable version and
    // activates that copy. The running legacy image is not moved or terminated.
    public static Task<bool> PrepareAsync(CancellationToken ct = default) =>
        Store.MigrateLegacyAsync(CurrentBundle.Version, ct);

    public static async Task<ProvisioningResult> ProvisionAsync(
        IProgress<Downloader.DownloadProgress> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(InstallDirectory);
        string zipPath = Path.Combine(InstallDirectory, $"bundle-{Guid.NewGuid():N}.zip");
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
    public static BackendProcessSpec? ServeSpec() => Store.ResolveActiveSpec();

    internal static IBackendProviderCatalog ProviderCatalog => Store;
}
