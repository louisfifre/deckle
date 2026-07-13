using System;
using System.Collections.Generic;

using Deckle.Core;
using Deckle.Transcription;

namespace Deckle.Setup;

// ── SetupContext ─────────────────────────────────────────────────────────────
//
// State shared across the wizard's pages. Each page reads its initial
// state from here, mutates user choices, and (in the install page) writes
// back the per-item results. The Window observes this object to enable/
// disable Next, format the summary on the final page, etc.
//
// Lives in Deckle.Setup itself — the wizard module owns its own runtime
// state. SetupContext used to sit in Deckle.Transcription but the
// only consumers are the wizard pages here, and keeping it in the
// transcription module forced parent ↔ child cycles once Whisper-specific
// catalogs (SpeechModels) migrated to Deckle.Transcription.Whisper.
//
// Plain POCO — no Action delegates for navigation. Pages call
// `Frame.Navigate(typeof(NextPage), context)` directly, which keeps the
// setup classes free of UI types.
public sealed class SetupContext
{
    // Where the data root lives. In the normal in-app wizard this is whatever
    // AppPaths resolved at start-up (LOCALAPPDATA or env-var override) and
    // never changes. In install mode the Folders page overwrites it with the
    // user's data-folder choice, so the Choices recap shows the real target.
    public string Location { get; set; } = AppPaths.UserDataRoot;

    // ── Install mode (the wizard as installer) ──────────────────────────────
    //
    // True when the wizard was launched by the download stub (`Deckle.exe
    // --install`), running from the extracted payload in a temp folder. The
    // flow gains a Folders step, ends in a Deploy step (copy + integrate +
    // relaunch from the install folder) instead of the provisioning step, and
    // the presence choice is written into the CHOSEN data root rather than
    // through AppPaths — which froze on the default root in this process.
    public bool InstallMode { get; init; }

    // The extracted payload the temp process runs from — what Deploy copies
    // into the install folder.
    public string SourceDirectory { get; init; } = System.AppContext.BaseDirectory;

    // The stub exe that launched us (`--stub <path>`). Deploy copies it into
    // the install folder as the uninstaller. Null on a dev launch of
    // `--install` without a stub — integration then skips the Installed-apps
    // entry, which would otherwise point at a missing uninstaller.
    public string? StubPath { get; init; }

    // The stub's temp root (`--cleanup <path>`), forwarded to the installed
    // process so it can delete the extraction once the wizard is done with it.
    public string? CleanupDirectory { get; init; }

    // The two folders the Folders page collects. App = binaries (per user),
    // Data = models/settings/logs, relocatable off a saturated C:.
    public string InstallDirectory { get; set; } = Deckle.Install.InstallPaths.DefaultInstallDir;
    public string DataDirectory { get; set; } = Deckle.Install.InstallPaths.DefaultDataDir;

    // The module selection the install plan is built from. Seeded by
    // SetupWindow from the recorded presence choice (or the full catalogue
    // when none is recorded), then overwritten by the Modules page when the
    // user commits a new selection.
    public IReadOnlySet<string> SelectedModules { get; set; } =
        new HashSet<string>(StringComparer.Ordinal);

    // Speech model the user picked in the Choices page. Null until the
    // wizard page initializes it from the active backend's catalog —
    // SetupContext itself does not couple to any specific backend, so it
    // never holds a hard reference to a Whisper or Voxtral default.
    public ModelEntry? SelectedModel { get; set; }

    // True after the user has clicked Install on the Choices page — gates
    // the transition to the Installing page.
    public bool ChoicesConfirmed { get; set; }

    // Per-item results captured by the Installing page, displayed on the
    // Summary page. Populated in the install plan's order.
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
