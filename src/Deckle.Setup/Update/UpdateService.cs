using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Install;

namespace Deckle.Setup;

// ── UpdateService ─────────────────────────────────────────────────────────────
//
// The silent update check and its one piece of state. The App runs CheckAsync
// in the background (at boot, then daily, gated by the Settings opt-out); a
// newer release parks in Available, from which every surface reads — the toast
// prompt, the Settings version row, and the download page the user's explicit
// click opens. Checking never downloads anything: the payload moves only on
// that click (UpdateDownloadPage), and the binary swap belongs to the new
// payload itself (Deckle.exe --update-apply), mirroring the install chain.
//
// The check runs only for a launch of the INSTALLED copy: the registered
// Installed-apps entry names the install folder, and the running image must be
// that folder's Deckle.exe. A dev build running from a worktree shares the
// registry hive but must neither offer nor apply updates over the dev tree —
// neophytes get the updater, maintainers keep the repo scripts.
public static class UpdateService
{
    public sealed record AvailableUpdate(
        string Tag, string Version, string ZipUrl, string Sha256Url, long ZipSize);

    // The newest release found by the last successful check, when it is newer
    // than the installed version; null while up to date (or never checked).
    public static AvailableUpdate? Available { get; private set; }

    // True when this process is the registered install's own Deckle.exe.
    public static bool IsInstalledLaunch()
    {
        if (UninstallEntry.Read() is not { } existing) return false;
        if (Environment.ProcessPath is not { } exe) return false;
        string installed = Path.Combine(existing.InstallDir, "Deckle.exe");
        return string.Equals(
            Path.GetFullPath(exe), Path.GetFullPath(installed),
            StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsInstalledLaunch())
        {
            DeckleSetupSource.Log.UpdateCheckSkippedDetail("not_an_installed_launch");
            return null;
        }

        string installedVersion = UninstallEntry.Read()!.Version;
        try
        {
            var release = await ReleaseResolver.ResolveLatestAsync(ct).ConfigureAwait(false);
            string latestVersion = ReleaseResolver.BareVersion(release.Tag);

            bool newer = Version.TryParse(installedVersion, out var current)
                && Version.TryParse(latestVersion, out var latest)
                && latest > current;

            if (newer)
            {
                Available = new AvailableUpdate(
                    release.Tag, latestVersion, release.ZipUrl, release.Sha256Url, release.ZipSize);
                DeckleSetupSource.Log.UpdateAvailable();
            }
            else
            {
                Available = null;
                DeckleSetupSource.Log.UpdateUpToDate();
            }
            DeckleSetupSource.Log.UpdateCheckDetail(installedVersion, latestVersion, newer);

            return Available;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Offline is a normal state for a local-first app — a failed check
            // is a warning in the narrative, never a surface the user sees.
            DeckleSetupSource.Log.UpdateCheckFailed();
            DeckleSetupSource.Log.UpdateCheckFailedDetail($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
