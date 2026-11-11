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
    // The release stage ("Alpha", "Beta") the build carries, for the title bar
    // subtitle. Unwired or empty leaves the subtitle off.
    public static Func<string>? GetAppStage;
    public static Func<string?>? GetAvailableUpdateVersion;
    public static Action? StartUpdate;

    // The App restarts the process in its dedicated --relocate-data mode, since
    // the live app cannot copy the root its own sinks and engines hold open.
    // Unwired (null) hides the Move affordance.
    public static Action<string>? RelocateDataRoot;

    // Unwired (null) shows every hotkey card.
    public static Func<string, bool>? IsHotkeyActionAvailable;

    // The shortcuts the rewrite profiles carry, set in each profile on the
    // Rewriting page. Deckle.Settings does not reference that module, so the
    // App lists them; the Shortcuts page shows them and checks its own chords
    // against them. Unwired (null) lists none.
    public static Func<IReadOnlyList<ProfileHotkey>>? ProfileHotkeys;

    // The developer mode's interface capture: every open Deckle window, then
    // every settings page, to PNG files. The App owns the windows and the
    // capture (Deckle.Vision), so it runs the whole of it; the answer is null
    // when developer mode is off. Unwired (null) hides the capture card.
    public static Func<Task<InterfaceCaptureOutcome?>>? CaptureInterface;
}

public sealed record ProfileHotkey(string ProfileName, Deckle.Core.HotkeyChord Chord);

// What an interface capture wrote: the folder, the images saved in it, and the
// windows or pages that could not be captured (listed in its index.json).
public sealed record InterfaceCaptureOutcome(string Folder, int Captured, int Failed);
