namespace Deckle.Installer;

// ── InstallPaths ──────────────────────────────────────────────────────────────
//
// The two locations the installer reasons about, and their defaults. The split
// is the whole point of the install UX (cf. the install-location decision):
//
//   • Binaries  — the app folder (~300 MB). Per-user, no admin, VS Code / Discord
//     style. Default %LOCALAPPDATA%\Programs\Deckle, changeable.
//
//   • Data      — Whisper models (up to ~3 GB), settings, corpus. The real volume
//     concern. Default %LOCALAPPDATA%\Deckle (what AppPaths uses), relocatable off
//     a saturated C: via the DECKLE_DATA_ROOT environment variable the app already
//     honours. When the user picks a non-default data folder, the installer sets
//     that variable; otherwise it leaves the app's own default untouched.
//
// %LOCALAPPDATA%\Programs is the documented per-user install root and never
// requires elevation — C:\Program Files is deliberately avoided (forces C: + an
// admin prompt = double friction for a family machine).
internal static class InstallPaths
{
    public static string DefaultInstallDir => Path.Combine(LocalAppData, "Programs", "Deckle");

    public static string DefaultDataDir => Path.Combine(LocalAppData, "Deckle");

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
