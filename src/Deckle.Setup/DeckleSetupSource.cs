using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

// Setup wizard provider. Covers first-run wizard pages under
// src/Deckle/Shell/Setup/: ChoicesPage (selection of items to download),
// InstallingPage (download orchestration + verification), SummaryPage (final
// summary), SetupWindow (window lifecycle).
//
// Provider Name = "Deckle-Setup" → [SETUP] tag through the bridge. Legacy used
// LogSource.Setup (= "SETUP") for exactly this scope.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info/Warning is a
// short Capital sentence with no IDs and no k=v; the technical detail (item id,
// byte count, duration, sha256, error) lives in a Verbose mirror that FOLLOWS
// it. The native runtime download keeps its own events (it has two failure
// modes — placeholder url, incomplete bundle — that the model items don't); the
// model items share the generic Item* events keyed by id.
[EventSource(Name = "Deckle-Setup")]
public sealed partial class DeckleSetupSource : DeckleEventSource
{
    public static readonly DeckleSetupSource Log = new();

    private DeckleSetupSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Milestones 4-19, their Verbose mirrors 20-32. 1/2/3 — the old generic
    // SetupInfo / SetupWarning / SetupError channels split 1:many into typed
    // events with no single successor; burned, never reused. IDs are public in
    // the ETW manifest; never reuse an id after deleting an event.
    public const int EvtWizardOpening              = 4;
    public const int EvtWizardCancelled            = 5;
    public const int EvtWindowOpened               = 6;
    public const int EvtWindowClosing              = 7;
    public const int EvtNativeSourcePicked         = 8;
    public const int EvtNativeImportFailed         = 9;
    public const int EvtChoicesConfirmed           = 10;
    public const int EvtNativeInstalled            = 11;
    public const int EvtNativeDownloadFailed       = 12;
    public const int EvtNativeRuntimeAborted       = 13;
    public const int EvtNativeBundleIncomplete     = 14;
    public const int EvtNativeCancelled            = 15;
    public const int EvtItemInstalled              = 16;
    public const int EvtItemDownloadFailed         = 17;
    public const int EvtItemCancelled              = 18;
    public const int EvtSummaryShown               = 19;
    public const int EvtWizardOpeningDetail        = 20;
    public const int EvtWindowClosingDetail        = 21;
    public const int EvtNativeSourcePickedDetail   = 22;
    public const int EvtNativeImportFailedDetail   = 23;
    public const int EvtChoicesConfirmedDetail     = 24;
    public const int EvtNativeInstalledDetail      = 25;
    public const int EvtNativeDownloadFailedDetail = 26;
    public const int EvtNativeRuntimeAbortedDetail = 27;
    public const int EvtNativeBundleIncompleteDetail = 28;
    public const int EvtItemInstalledDetail        = 29;
    public const int EvtItemDownloadFailedDetail   = 30;
    public const int EvtItemCancelledDetail        = 31;
    public const int EvtSummaryShownDetail         = 32;
    // Install mode (the wizard as installer) — Folders + Deploy pages.
    public const int EvtFoldersChosen              = 33;
    public const int EvtFoldersChosenDetail        = 34;
    public const int EvtDeployCompleted            = 35;
    public const int EvtDeployCompletedDetail      = 36;
    public const int EvtDeployFailed               = 37;
    public const int EvtDeployFailedDetail         = 38;
    public const int EvtDeployBlockedByRunningApp  = 39;
    // In-app updater — silent check, download page, handoff to --update-apply.
    public const int EvtUpdateUpToDate             = 40;
    public const int EvtUpdateAvailable            = 41;
    public const int EvtUpdateCheckDetail          = 42;
    public const int EvtUpdateCheckFailed          = 43;
    public const int EvtUpdateCheckFailedDetail    = 44;
    public const int EvtUpdateCheckSkippedDetail   = 45;
    public const int EvtUpdateDownloadStarted      = 46;
    public const int EvtUpdateDownloadStartedDetail = 47;
    public const int EvtUpdateDownloadFailed       = 48;
    public const int EvtUpdateDownloadFailedDetail = 49;
    public const int EvtUpdateHandoff              = 50;
    public const int EvtUpdateHandoffDetail        = 51;
    // Data-root relocation (--relocate-data, RelocatePage).
    public const int EvtRelocateStarted            = 52;
    public const int EvtRelocateStartedDetail      = 53;
    public const int EvtRelocateCompleted          = 54;
    public const int EvtRelocateCompletedDetail    = 55;
    public const int EvtRelocateFailed             = 56;
    public const int EvtRelocateFailedDetail       = 57;

}
