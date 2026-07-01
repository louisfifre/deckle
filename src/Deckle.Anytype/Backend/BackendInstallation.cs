using System.IO;

namespace Deckle.Anytype;

// ── BackendInstallation ──────────────────────────────────────────────────────
//
// Where the headless anytype-cli lives on this machine, and the serve spec that
// runs it. This is the concrete half of the BackendProcessSpec seam: the
// provisioning step downloads the pinned binary here (JOURNAL 2026-06-19), and
// everything downstream — task registration, the wizard's predicates — asks
// this class rather than re-deriving the path.
//
// The location follows the frozen layout split (JOURNAL 2026-06-18):
// executables under %LOCALAPPDATA%\Programs\Deckle, user data and credentials
// under %LOCALAPPDATA%\Deckle. The installer's InstallPaths owns the same root
// but is internal to Deckle.Installer; the anytype subfolder is this module's,
// so the module resolves it itself.
public static class BackendInstallation
{
    public static string ExecutablePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Deckle", "anytype", "anytype.exe");

    // The provisioning predicate: has the pinned binary been downloaded? False
    // means the module stays dormant — never a boot failure.
    public static bool IsInstalled() => File.Exists(ExecutablePath);

    // The serve invocation the scheduled task hosts. --no-update-check because
    // the version pin is Deckle's (known-good + signal newer, never
    // auto-update — JOURNAL 2026-06-18); the CLI must not self-nag or
    // self-move. No embedded paths, so no quoting concerns in the arguments.
    public static BackendProcessSpec ServeSpec() =>
        new(ExecutablePath, "serve --no-update-check");
}
