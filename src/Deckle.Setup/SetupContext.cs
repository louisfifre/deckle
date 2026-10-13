using System;
using System.Collections.Generic;

using Deckle.Core;
using Deckle.Transcription;

namespace Deckle.Setup;

// State shared across the wizard's pages: the user's choices and the per-item
// install results. A plain POCO with no UI types and no navigation delegates;
// pages navigate the window's frame themselves.
public sealed class SetupContext
{
    // Where the data root lives. In the in-app wizard this is whatever
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
    // In update mode the download page fills it in flight: the temp root it
    // extracted the new payload into rides the same forwarding chain.
    public string? CleanupDirectory { get; set; }

    // ── Update mode (the wizard as updater) ─────────────────────────────────
    //
    // True on the two legs of an in-app update: the running app's download
    // page (fetch + verify + extract + handoff), and the new payload's
    // `--update-apply` process, which runs DeployPage over the live install —
    // same gate/copy/integrate steps, folders and modules taken as they are
    // instead of asked. PendingUpdate carries the release the check resolved;
    // it is only read on the download leg.
    public bool UpdateMode { get; init; }
    public UpdateService.AvailableUpdate? PendingUpdate { get; init; }

    // Exact process that handed an update to the new payload. Deploy waits on
    // its native exit signal before scanning for any remaining blockers.
    public int? PredecessorProcessId { get; init; }

    // ── Relocate mode (the wizard as data mover) ────────────────────────────
    //
    // True in the dedicated `--relocate-data` process, which runs RelocatePage
    // alone; DataDirectory is reused as the move's target.
    public bool RelocateMode { get; init; }

    public string InstallDirectory { get; set; } = Deckle.Install.InstallPaths.DefaultInstallDir;
    public string DataDirectory { get; set; } = Deckle.Install.InstallPaths.DefaultDataDir;

    // The module selection the install plan is built from. Seeded by the
    // wizard host from the recorded presence choice (or the full catalogue
    // when none is recorded), then overwritten by the Modules page.
    public IReadOnlySet<string> SelectedModules { get; set; } =
        new HashSet<string>(StringComparer.Ordinal);

    // Null until the wizard host seeds it: SetupContext stays backend-agnostic
    // and holds no ASR catalog default.
    public ModelEntry? SelectedModel { get; set; }

    public bool ChoicesConfirmed { get; set; }

    // Per-item results captured by the Installing page, in the install plan's
    // order.
    public List<InstallResult> Results { get; } = new();

    public bool AllSucceeded
    {
        get
        {
            foreach (var r in Results) if (!r.Success) return false;
            return Results.Count > 0;
        }
    }
}

// One row on the Summary page. Bytes is the installed size after success, null
// when the item reports no size.
public sealed record InstallResult(
    string ItemId,
    string DisplayName,
    bool Success,
    string? ErrorMessage,
    long? Bytes);
