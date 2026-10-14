using Deckle.Catalog;

namespace Deckle.Settings;

// ── AppSettings ───────────────────────────────────────────────────────────────
//
// Shell-only settings root persisted to <UserDataRoot>/settings.json; each
// module persists its own modules/<id>/settings.json. Mutations to `Current`
// go through SettingsService (debounced Save).
public sealed class AppSettings
{
    public PathsSettings       Paths       { get; set; } = new();
    public AppearanceSettings  Appearance  { get; set; } = new();
    public OverlaySettings     Overlay     { get; set; } = new();
    public PasteSettings       Paste       { get; set; } = new();
    public UpdatesSettings     Updates     { get; set; } = new();
    public HotkeysSettings     Hotkeys     { get; set; } = new();
}

// null = unbound. The initializers are the single source of defaults: the
// composer's reset reads them. Record defaults to Win + the physical key left
// of "1" (scancode 0x29).
public sealed class HotkeysSettings
{
    public HotkeyChord? Record          { get; set; } = new(HotkeyModifiers.Win, 0x29);
    public HotkeyChord? AmbientLight    { get; set; }
    public HotkeyChord? Autocorrect     { get; set; }
    public HotkeyChord? TaskbarCover    { get; set; }
    public HotkeyChord? PrecisionScroll { get; set; }
    public HotkeyChord? Rewrite         { get; set; }
    public HotkeyChord? Trackpad        { get; set; }
}

// The silent update check (UpdateService, run by the App at boot then daily).
// On by default with this opt-out — the one background network call the app
// makes without a user act, so it stays a user's choice to keep.
public sealed class UpdatesSettings
{
    public bool AutoCheckEnabled { get; set; } = true;
}

// Auto-paste after transcription. Off by default — the clipboard is the safe
// default and the user explicitly opts in to SendInput-driven paste.
public sealed class PasteSettings
{
    public bool AutoPasteEnabled { get; set; } = false;
}

// Theme = "System" | "Light" | "Dark". ShowDescriptions is the Settings
// window's Info toggle (Catalog SettingsDescriptions): off, the pages show
// their cards without the long text under each header (2026-10-14).
public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "System";
    public bool ShowDescriptions { get; set; } = false;
}

// HUD overlay displayed during recording/transcription.
// Position = "BottomCenter" | "TopCenter"; readers treat any value starting
// with "Top" as TopCenter, anything else as BottomCenter.
public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;
    public bool FadeOnProximity { get; set; } = true;
    public string Position { get; set; } = "BottomCenter";

    // Controls Deckle-authored functional HUD feedback: message movement,
    // processing-material rotations, state blends and microphone response.
    // Simple fades are excluded and follow the Windows animation preference.
    public bool Animations { get; set; } = true;

    // "Left" | "Right": which side of the primary display the live
    // transcript panel sits on.
    public string TranscriptPanelSide { get; set; } = "Right";
}

// BackupDirectory: where SettingsBackupService writes snapshots; empty falls
// back to AppPaths.SettingsBackupDirectory. The user can point it at a synced
// folder (OneDrive) to carry backups across machines.
public sealed class PathsSettings
{
    public string BackupDirectory { get; set; } = "";
}
