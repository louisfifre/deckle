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
public sealed class DeckleSetupSource : DeckleEventSource
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

    // ── Wizard lifecycle ──────────────────────────────────────────────────────

    [Event(EvtWizardOpening,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Opening the first-run setup wizard")]
    public void WizardOpening()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWizardOpening);
    }

    [Event(EvtWizardOpeningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "first run gate | natives_installed={0} | default_model_installed={1}")]
    public void WizardOpeningDetail(bool natives_installed, bool default_model_installed)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWizardOpeningDetail, natives_installed, default_model_installed);
    }

    [Event(EvtWizardCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup wizard was cancelled")]
    public void WizardCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWizardCancelled);
    }

    [Event(EvtWindowOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup window opened")]
    public void WindowOpened()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowOpened);
    }

    [Event(EvtWindowClosing,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup window is closing")]
    public void WindowClosing()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowClosing);
    }

    [Event(EvtWindowClosingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup window closing | success={0}")]
    public void WindowClosingDetail(bool success)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowClosingDetail, success);
    }

    // ── Choices page ──────────────────────────────────────────────────────────

    [Event(EvtNativeSourcePicked,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A native runtime source was picked")]
    public void NativeSourcePicked()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeSourcePicked);
    }

    [Event(EvtNativeSourcePickedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native source | source={0} | copied={1}")]
    public void NativeSourcePickedDetail(string source, int copied)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeSourcePickedDetail, source, copied);
    }

    [Event(EvtNativeImportFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Importing the native runtime failed")]
    public void NativeImportFailed()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeImportFailed);
    }

    [Event(EvtNativeImportFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native import failed | error={0}")]
    public void NativeImportFailedDetail(string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeImportFailedDetail, error);
    }

    [Event(EvtChoicesConfirmed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Setup choices were confirmed")]
    public void ChoicesConfirmed()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtChoicesConfirmed);
    }

    [Event(EvtChoicesConfirmedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "choices confirmed | location={0} | model={1}")]
    public void ChoicesConfirmedDetail(string location, string model)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtChoicesConfirmedDetail, location, model);
    }

    // ── Native runtime download (InstallingPage) ──────────────────────────────

    [Event(EvtNativeInstalled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime was installed")]
    public void NativeInstalled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeInstalled);
    }

    [Event(EvtNativeInstalledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native ok | bundle={0} | bytes={1} | dur_ms={2} | sha256={3}")]
    public void NativeInstalledDetail(string bundle, long bytes, long dur_ms, string sha256)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeInstalledDetail, bundle, bytes, dur_ms, sha256);
    }

    [Event(EvtNativeDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime download failed")]
    public void NativeDownloadFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeDownloadFailed);
    }

    [Event(EvtNativeDownloadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native download failed | error={0}")]
    public void NativeDownloadFailedDetail(string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeDownloadFailedDetail, error);
    }

    [Event(EvtNativeRuntimeAborted,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime download was aborted")]
    public void NativeRuntimeAborted()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeRuntimeAborted);
    }

    [Event(EvtNativeRuntimeAbortedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native runtime aborted | reason={0}")]
    public void NativeRuntimeAbortedDetail(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeRuntimeAbortedDetail, reason);
    }

    [Event(EvtNativeBundleIncomplete,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime bundle is incomplete")]
    public void NativeBundleIncomplete()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeBundleIncomplete);
    }

    [Event(EvtNativeBundleIncompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native incomplete | extracted={0} | expected={1}")]
    public void NativeBundleIncompleteDetail(int extracted, int expected)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeBundleIncompleteDetail, extracted, expected);
    }

    [Event(EvtNativeCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime download was cancelled")]
    public void NativeCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeCancelled);
    }

    // ── Model item download (InstallingPage) ──────────────────────────────────

    [Event(EvtItemInstalled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item was installed")]
    public void ItemInstalled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemInstalled);
    }

    [Event(EvtItemInstalledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item ok | id={0} | bytes={1} | dur_ms={2} | sha256={3}")]
    public void ItemInstalledDetail(string id, long bytes, long dur_ms, string sha256)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemInstalledDetail, id, bytes, dur_ms, sha256);
    }

    [Event(EvtItemDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item failed to download")]
    public void ItemDownloadFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemDownloadFailed);
    }

    [Event(EvtItemDownloadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item failed | id={0} | error={1}")]
    public void ItemDownloadFailedDetail(string id, string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemDownloadFailedDetail, id, error);
    }

    [Event(EvtItemCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item download was cancelled")]
    public void ItemCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemCancelled);
    }

    [Event(EvtItemCancelledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item cancelled | id={0}")]
    public void ItemCancelledDetail(string id)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemCancelledDetail, id);
    }

    // ── Install mode (Folders + Deploy pages) ─────────────────────────────────

    [Event(EvtFoldersChosen,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The install folders were chosen")]
    public void FoldersChosen()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFoldersChosen);
    }

    [Event(EvtFoldersChosenDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "folders chosen | app={0} | data={1}")]
    public void FoldersChosenDetail(string app, string data)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFoldersChosenDetail, app, data);
    }

    [Event(EvtDeployCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Deckle was placed and registered")]
    public void DeployCompleted()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployCompleted);
    }

    [Event(EvtDeployCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "deploy ok | app={0} | data={1} | bytes={2} | dur_ms={3}")]
    public void DeployCompletedDetail(string app, string data, long bytes, long dur_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployCompletedDetail, app, data, bytes, dur_ms);
    }

    [Event(EvtDeployFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Placing Deckle failed")]
    public void DeployFailed()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployFailed);
    }

    [Event(EvtDeployFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "deploy failed | step={0} | error={1}")]
    public void DeployFailedDetail(string step, string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployFailedDetail, step, error);
    }

    [Event(EvtDeployBlockedByRunningApp,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The install is blocked by a running Deckle")]
    public void DeployBlockedByRunningApp()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployBlockedByRunningApp);
    }

    // ── Summary page ──────────────────────────────────────────────────────────

    [Event(EvtSummaryShown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup summary was shown")]
    public void SummaryShown()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSummaryShown);
    }

    [Event(EvtSummaryShownDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "summary | success={0} | items={1}")]
    public void SummaryShownDetail(bool success, int items)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSummaryShownDetail, success, items);
    }
}
