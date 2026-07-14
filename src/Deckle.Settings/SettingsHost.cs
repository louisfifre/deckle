using Microsoft.UI.Xaml;

namespace Deckle.Settings;

// ── SettingsHost ──────────────────────────────────────────────────────────
//
// App-side hooks the Settings UI pages and ViewModels need to drive
// concerns that only the App owns: theme broadcast across windows,
// process restart, the lazy SettingsWindow instance accessor used by
// dialogs to anchor their XamlRoot, and re-opening the first-run setup
// wizard on demand.
//
// Why a static delegate registry rather than a project reference back
// to the App assembly? Because that would close the dependency cycle
// (App → Deckle.Settings → App). The pattern mirrors what
// `HudChrono.MaxRecordingDurationSecondsProvider` does in
// Deckle.Hud: the lib exposes static fields, the App wires them
// once at boot, the lib's call sites invoke them with `?.Invoke(...)`
// and degrade silently to no-op when nothing is wired (so the lib
// remains buildable / testable in isolation).
//
// All five hooks are intentionally `Action<...>` / `Func<...>` rather
// than a single interface — keeps the surface minimal, no boxing, and
// each hook can be wired independently if a future host implements only
// part of the contract (e.g. a settings preview window without a full
// app shell).
public static class SettingsHost
{
    // Broadcast the requested theme ("Light" | "Dark" | "System") to
    // every long-lived window the host tracks. Wired by App in
    // OnLaunched; no-op until then.
    public static Action<string>? ApplyTheme;

    // Restart the process, optionally returning to a Settings page
    // tag (e.g. "Deckle.Transcription.WhisperPage, Deckle.Transcription" — assembly-
    // qualified for cross-assembly Type.GetType resolution) so the user
    // lands back on the page that triggered the restart. Wired by App.
    public static Action<string?>? RestartApp;

    // Accessor for the currently-open SettingsWindow so dialogs can
    // anchor their `XamlRoot` and resolve the parent hwnd. Returns
    // null when the window hasn't been lazily created yet.
    public static Func<Window?>? GetSettingsWindow;

    // Re-open the first-run setup wizard on demand (Browse model,
    // replace native runtime…). The wizard XAML and code live in the
    // standalone Deckle.Setup module — we go through a hook here so
    // Deckle.Settings doesn't take a back-reference to either Deckle.App
    // or Deckle.Setup just for the wizard entry point.
    public static Action? OpenSetupWizard;

    // Reports whether the speech runtime + default model are provisioned, so
    // the Dictation page can surface a "set up" call-to-action instead of its
    // tuning controls when they aren't. Answered by the App, which owns the
    // provisioning knowledge — Deckle.Transcription can't see the Whisper child
    // module. Null-safe: callers treat "unwired" as provisioned.
    public static Func<bool>? IsSpeechProvisioned;

    // Version row on the General page. The update knowledge lives in
    // Deckle.Setup (UpdateService) which this module must not reference —
    // the App answers through these hooks, same posture as the wizard entry
    // point above. GetAppVersion is the running build's display version;
    // GetAvailableUpdateVersion is the newer release the silent check parked
    // (null = none known); StartUpdate opens the explicit update flow.
    public static Func<string>? GetAppVersion;
    public static Func<string?>? GetAvailableUpdateVersion;
    public static Action? StartUpdate;
}
