using Microsoft.UI.Xaml;

namespace Deckle.Settings;

// ── SettingsHost ──────────────────────────────────────────────────────────
//
// App-side hooks for concerns only the App owns. A static delegate registry
// rather than a project reference back to the App assembly, which would close
// the dependency cycle (App → Deckle.Settings → App). The App wires them once
// at boot; call sites invoke them with `?.Invoke(...)` and degrade to a no-op
// when nothing is wired, so the lib stays buildable and testable in isolation.
// Separate delegates rather than one interface, so a partial host wires only
// what it needs.
public static class SettingsHost
{
    // Broadcast the requested theme ("Light" | "Dark" | "System") to
    // every long-lived window the host tracks.
    public static Action<string>? ApplyTheme;

    // Restart the process, optionally returning to a Settings page tag. The tag
    // is assembly-qualified (e.g. "Deckle.Transcription.WhisperPage,
    // Deckle.Transcription") for cross-assembly Type.GetType resolution.
    public static Action<string?>? RestartApp;

    // Null until the SettingsWindow is lazily created.
    public static Func<Window?>? GetSettingsWindow;

    // The wizard lives in Deckle.Setup, which Deckle.Settings does not reference.
    public static Action? OpenSetupWizard;

    // Whether the speech runtime + default model are provisioned, so the
    // Dictation page can offer setup instead of its tuning controls. Answered by
    // the App, which owns the provisioning knowledge — Deckle.Transcription
    // can't see the Whisper child module. Callers treat "unwired" as provisioned.
    public static Func<bool>? IsSpeechProvisioned;

    // The update knowledge lives in Deckle.Setup (UpdateService), which this
    // module must not reference. GetAvailableUpdateVersion is the newer release
    // the silent check parked (null = none known).
    public static Func<string>? GetAppVersion;
    public static Func<string?>? GetAvailableUpdateVersion;
    public static Action? StartUpdate;

    // The App restarts the process in its dedicated --relocate-data mode, since
    // the live app cannot copy the root its own sinks and engines hold open.
    // Unwired (null) hides the Move affordance.
    public static Action<string>? RelocateDataRoot;

    // Unwired (null) shows every hotkey card.
    public static Func<string, bool>? IsHotkeyActionAvailable;
}
