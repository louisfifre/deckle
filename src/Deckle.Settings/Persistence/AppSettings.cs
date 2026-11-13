using Deckle.Core;

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
    public UpdatesSettings     Updates     { get; set; } = new();
    public HotkeysSettings     Hotkeys     { get; set; } = new();
    public DeveloperSettings   Developer   { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
}

// The one shared signal for a machine that cannot spare much. Any module
// that has a cost it can lower reads PerformanceMode and picks its cheaper
// values from tiers it owns in code (Ambient's capture cap is the first
// reader). Off by default: a typical PC runs the normal tiers. It is a
// shell setting, not a module's, because no module acts on "the machine is
// weak"; each acts on its own cost.
public sealed class PerformanceSettings
{
    public bool PerformanceMode { get; set; } = false;
}

// The one door to everything an ordinary user has no use for. Off by default.
// While on, the App offers the surfaces still in alpha (the tray icon's
// right-click menu) and the General page shows the developer tools, the
// interface capture first. A surface that is not ready hides behind this switch rather than
// behind one of its own.
public sealed class DeveloperSettings
{
    public bool DeveloperMode { get; set; } = false;
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

// Theme = "System" | "Light" | "Dark". A ShowDescriptions key left in an older
// file is ignored: nothing reads it any more.
//
// Animations = "System" | "On" | "Reduced", for the animations Deckle draws
// itself (Deckle.Hud.AnimationPreference, ADR-0009). "System" follows the
// Windows "Animation effects" preference; the two others override it, because
// that one switch also serves people who only dislike Windows's own motion.
//
// AnimationsChoiceOffered records that the one-time notification offering
// Deckle's own animations, to a person whose Windows animations are off, has
// been shown (App.Animations.cs). Once shown, it never comes back.
public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "System";
    public string Animations { get; set; } = "System";
    public bool AnimationsChoiceOffered { get; set; }
}

// HUD overlay displayed during recording/transcription.
// Position = "BottomCenter" | "TopCenter"; readers treat any value starting
// with "Top" as TopCenter, anything else as BottomCenter.
public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;
    public bool FadeOnProximity { get; set; } = true;
    public string Position { get; set; } = "BottomCenter";

    // No animations switch lives here: every HUD animation follows
    // Appearance.Animations (Deckle.Hud.AnimationPreference). A settings file
    // written by an older build may still carry "animations"; the deserializer
    // ignores members this class no longer declares.

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
