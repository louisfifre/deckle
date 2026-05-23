using System.Collections.Generic;

using Deckle.Core;
using Deckle.Transcription.Setup;

namespace Deckle.Setup;

// ── SetupContext ─────────────────────────────────────────────────────────────
//
// State shared across the wizard's pages. Each page reads its initial
// state from here, mutates user choices, and (in the install page) writes
// back the per-item results. The Window observes this object to enable/
// disable Next, format the summary on the final page, etc.
//
// Lives in Deckle.Setup itself — the wizard module owns its own runtime
// state. SetupContext used to sit in Deckle.Transcription.Setup but the
// only consumers are the wizard pages here, and keeping it in the
// transcription module forced parent ↔ child cycles once Whisper-specific
// catalogs (SpeechModels) migrated to Deckle.Transcription.Whisper.
//
// Plain POCO — no Action delegates for navigation. Pages call
// `Frame.Navigate(typeof(NextPage), context)` directly, which keeps the
// setup classes free of UI types.
public sealed class SetupContext
{
    // Where to install. Defaults to whatever AppPaths resolved at start-up
    // (LOCALAPPDATA or env-var override). The wizard's location section
    // doesn't yet support changing this in-process — a custom path needs
    // an app restart in V1, so this stays read-only after construction.
    public string Location { get; init; } = AppPaths.UserDataRoot;

    // Speech model the user picked in the Choices page. Null until the
    // wizard page initializes it from the active backend's catalog —
    // SetupContext itself does not couple to any specific backend, so it
    // never holds a hard reference to a Whisper or Voxtral default.
    public ModelEntry? SelectedModel { get; set; }

    // True after the user has clicked Install on the Choices page — gates
    // the transition to the Installing page.
    public bool ChoicesConfirmed { get; set; }

    // Per-item results captured by the Installing page, displayed on the
    // Summary page. Populated in order: native runtime first, then the
    // chosen model, then the VAD model.
    public List<InstallResult> Results { get; } = new();

    // True when every Results entry is Success — drives the Summary page's
    // success vs error rendering.
    public bool AllSucceeded
    {
        get
        {
            foreach (var r in Results) if (!r.Success) return false;
            return Results.Count > 0;
        }
    }
}

// ── InstallResult ────────────────────────────────────────────────────────────
//
// One row on the Summary page. Captures what the wizard tried, whether
// it worked, and (on failure) the human-readable reason. Bytes is the
// installed size after success — null for native runtime entries that
// only count files.
public sealed record InstallResult(
    string ItemId,
    string DisplayName,
    bool Success,
    string? ErrorMessage,
    long? Bytes);
