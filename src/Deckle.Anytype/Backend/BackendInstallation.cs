using System.IO;
using Deckle.Core;
using Deckle.Install;

namespace Deckle.Anytype;

// Where the headless anytype-cli lives on this machine, the pinned bundle the
// wizard downloads, and the serve spec that runs it. This is the concrete half
// of the BackendProcessSpec seam: everything downstream — the supervisor's
// spawn, the wizard's predicates and install step — asks this class rather
// than re-deriving paths or the pin.
public static class BackendInstallation
{
    // Provider executables deliberately live beside, not inside, the replaceable
    // Deckle payload. They also stay outside DECKLE_DATA_ROOT: relocating user
    // data must never move a running image.
    public static string ProvidersDirectory { get; } = InstallPaths.DefaultProvidersDir;

    public static string InstallDirectory { get; } = Path.Combine(ProvidersDirectory, "Anytype");

    // The legacy in-payload provider path. Updates preserve this directory only
    // while its executable is still running; reconciliation may adopt that PID,
    // while every new spawn resolves the activated external copy.
    public static string LegacyInstallDirectory { get; } = InstallPaths.LegacyAnytypeProviderDir;

    private static BackendProviderStore Store { get; } =
        new(InstallDirectory, LegacyInstallDirectory);

    public sealed record BackendBundle(
        string Version,
        string Url,
        string Sha256,
        long SizeBytes,
        string DisplayName);

    // The last version that ever lived in the legacy install path. A legacy
    // payload is always this version, whatever CurrentBundle pins today, so
    // migration must label it with this constant and never with the pin.
    internal const string LegacyBundleVersion = "0.3.6";

    // The version pin (known-good, never auto-update). Installing the binary is
    // only half the first run: the bot account and API key are a separate,
    // interactive provisioning act.
    //
    // The official Windows assets since 0.3.7 die with 0xC0000374 at account
    // open (msvcrt-linked exe plus a cross-allocator free in tantivy-go, see
    // anyproto/tantivy-go#25). Until upstream ships the fix, the pin is our own
    // rebuild of the official tag, produced by
    // scripts/commands/release/build-anytype-backend.ps1 and hosted as a
    // release asset of this repository; the SHA-256 is the one that script
    // prints. Once a release carries the fix, point this back at
    // github.com/anyproto/anytype-cli and pin GitHub's per-asset digest.
    public static BackendBundle CurrentBundle { get; } = new(
        Version:     "0.5.0",
        Url:         "https://github.com/louisfifre/deckle/releases/download/anytype-backend-v0.5.0/anytype-cli-v0.5.0-windows-amd64.zip",
        Sha256:      "988760efb3fa29d378534baee71b3433fdceca72dac96362ce65d076029ceb2e",
        SizeBytes:   51_350_092L,
        DisplayName: "Anytype backend (anytype-cli)");

    // False means the module stays dormant — never a boot failure — and the
    // wizard downloads the pin.
    public static bool IsInstalled() => Store.IsInstalled(CurrentBundle.Version);

    // The zip must already be checksum-verified. Its internal layout is not
    // contractual: the folder holding the exe is what gets installed, wherever
    // it sits in the archive.
    public static Task<bool> InstallFromZipAsync(string zipPath, CancellationToken ct) =>
        Store.InstallFromZipAsync(zipPath, CurrentBundle.Version, ct);

    // Copies the already verified legacy payload into an immutable version and
    // activates that copy. The running legacy image is not moved or terminated.
    public static Task<bool> PrepareAsync(CancellationToken ct = default) =>
        Store.MigrateLegacyAsync(LegacyBundleVersion, ct);

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

    // The serve arguments carry --no-update-check because the version pin is
    // Deckle's (known-good, never auto-update); the CLI must not self-nag or
    // self-move.
    public static BackendProcessSpec? ServeSpec() => Store.ResolveActiveSpec();

    internal static IBackendProviderCatalog ProviderCatalog => Store;
}
