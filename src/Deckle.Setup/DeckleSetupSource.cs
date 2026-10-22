using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

[EventSource(Name = "Deckle-Setup")]
public sealed partial class DeckleSetupSource : DeckleEventSource
{
    public static readonly DeckleSetupSource Log = new();

    private DeckleSetupSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // IDs 1-3 are burned. IDs are public in the ETW manifest; never reuse an
    // id after deleting an event.
    // 4 retired.
    // 5 retired.
    public const int EvtWindowOpened               = 6;
    public const int EvtWindowClosing              = 7;
    public const int EvtNativeSourcePicked         = 8;
    // 9 retired.
    public const int EvtChoicesConfirmed           = 10;
    // 11 retired.
    // 12 retired.
    // 13 retired.
    // 14 retired.
    // 15 retired.
    public const int EvtItemInstalled              = 16;
    // 17 retired.
    public const int EvtItemCancelled              = 18;
    public const int EvtSummaryShown               = 19;
    // 20 retired.
    public const int EvtWindowClosingDetail        = 21;
    public const int EvtNativeSourcePickedDetail   = 22;
    // 23 retired.
    public const int EvtChoicesConfirmedDetail     = 24;
    // 25 retired.
    // 26 retired.
    // 27 retired.
    // 28 retired.
    public const int EvtItemInstalledDetail        = 29;
    // 30 retired.
    public const int EvtItemCancelledDetail        = 31;
    public const int EvtSummaryShownDetail         = 32;
    // Install mode (the wizard as installer) — Folders + Deploy pages.
    public const int EvtFoldersChosen              = 33;
    public const int EvtFoldersChosenDetail        = 34;
    public const int EvtDeployCompleted            = 35;
    public const int EvtDeployCompletedDetail      = 36;
    // 37 retired.
    // 38 retired.
    // 39 retired.
    // In-app updater — silent check, download page, handoff to --update-apply.
    public const int EvtUpdateUpToDate             = 40;
    public const int EvtUpdateAvailable            = 41;
    public const int EvtUpdateCheckDetail          = 42;
    // 43 retired.
    // 44 retired.
    public const int EvtUpdateCheckSkippedDetail   = 45;
    public const int EvtUpdateDownloadStarted      = 46;
    public const int EvtUpdateDownloadStartedDetail = 47;
    // 48 retired.
    // 49 retired.
    public const int EvtUpdateHandoff              = 50;
    public const int EvtUpdateHandoffDetail        = 51;
    // Data-root relocation (--relocate-data, RelocatePage).
    public const int EvtRelocateStarted            = 52;
    public const int EvtRelocateStartedDetail      = 53;
    public const int EvtRelocateCompleted          = 54;
    public const int EvtRelocateCompletedDetail    = 55;
    // 56 retired.
    // 57 retired.
    public const int EvtNativeImportFailed         = 58;
    public const int EvtItemStarted                = 59;
    public const int EvtItemSkippedDetail          = 60;
    public const int EvtItemDownloadFailed         = 61;
    public const int EvtDeployStarted              = 62;
    public const int EvtDeployFailed               = 63;
    public const int EvtDeployBlockedByRunningApp  = 64;
    public const int EvtUpdateCheckFailed          = 65;
    public const int EvtUpdateDownloadFailed       = 66;
    public const int EvtUpdateDownloadCompleted    = 67;
    public const int EvtUpdateDownloadCancelled    = 68;
    public const int EvtRelocateFailed             = 69;
    public const int EvtRelocateInsufficientSpaceDetail = 70;
    public const int EvtRelocateCancelled          = 71;
    public const int EvtBestEffortFailedDetail     = 72;
    public const int EvtPredecessorWaitTimedOut    = 73;
    public const int EvtChildProcessLaunchedDetail = 74;
    public const int EvtItemImportFailed           = 75;
    public const int EvtItemImported               = 76;
    public const int EvtRelocateFilesSkipped       = 77;
    public const int EvtPredecessorWaitFailed      = 78;

}
