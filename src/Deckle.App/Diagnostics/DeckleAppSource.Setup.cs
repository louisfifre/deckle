using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Transactional modes (install, update-apply, relocate) ───────────
    //
    // mode is the phase InstallModeEnteredDetail names: wizard, continuation,
    // update-apply, relocate. Each mode ends the process, so the exit line is
    // written just before Environment.Exit or the relaunch that ends it.

    // outcome: completed, cancelled or refused (the mode would not start on
    // what it was given; reason says why).
    [Event(EvtTransactionalModeExited,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Install mode ended")]
    public void TransactionalModeExited(string mode, string outcome, string reason, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtTransactionalModeExited, mode, outcome, reason, duration_ms);
    }

    [Event(EvtTransactionalModeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "An install mode run failed, so Deckle exits\nmode={0} | ex_type={1} | ex_message={2} | duration_ms={3}")]
    public void TransactionalModeFailed(string mode, string ex_type, string ex_message, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtTransactionalModeFailed, mode, ex_type, ex_message, duration_ms);
    }

    // Best-effort: a leftover temp folder costs disk space, nothing else.
    [Event(EvtInstallTempCleanupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The installer's temp folder could not be removed\npath={0} | ex_type={1} | ex_message={2}")]
    public void InstallTempCleanupFailed(string path, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtInstallTempCleanupFailed, path, ex_type, ex_message);
    }

    // ── Relocated data root cleanup ─────────────────────────────────────
    //
    // A recursive delete of a whole data root: Warning before it runs, so the
    // log names the folder even if the process dies mid-delete.

    [Event(EvtDataRootCleanupStarting,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle is deleting the previous data root\npath={0}")]
    public void DataRootCleanupStarting(string path)
    {
        if (IsEnabled()) WriteEvent(EvtDataRootCleanupStarting, path);
    }

    // outcome: completed, or skipped when a guard refused the folder
    // (reason: is_live_root, not_found, not_a_data_root).
    [Event(EvtDataRootCleanupFinished,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Previous data root cleanup ended")]
    public void DataRootCleanupFinished(string outcome, string reason, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDataRootCleanupFinished, outcome, reason, elapsed_ms);
    }

    [Event(EvtDataRootCleanupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The previous data root could not be fully deleted, so some of its files stay on disk\nex_type={0} | ex_message={1} | elapsed_ms={2}")]
    public void DataRootCleanupFailed(string ex_type, string ex_message, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDataRootCleanupFailed, ex_type, ex_message, elapsed_ms);
    }

    // ── Update checks and the in-app update flow ────────────────────────

    [Event(EvtUpdateChecksStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Background update checks started")]
    public void UpdateChecksStarted(int interval_h)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateChecksStarted, interval_h);
    }

    // outcome: disabled, up_to_date, available, already_prompted, failed.
    // UpdateService and the notification dispatcher narrate their own
    // failures, so a failed check stays Verbose here.
    [Event(EvtUpdateCheckOutcome,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "outcome={0} | version={1} | ex_type={2} | ex_message={3}")]
    public void UpdateCheckOutcome(string outcome, string version, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateCheckOutcome, outcome, version, ex_type, ex_message);
    }

    [Event(EvtUpdateChecksStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Background update checks stopped")]
    public void UpdateChecksStopped(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateChecksStopped, reason);
    }

    [Event(EvtUpdateFlowStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Update started")]
    public void UpdateFlowStarted(string version)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateFlowStarted, version);
    }

    // The person asked for the update (toast or Settings) and nothing opens.
    // reason: no_pending_update, install_not_registered.
    [Event(EvtUpdateFlowRefused,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The requested update could not start, so nothing was installed\nreason={0}")]
    public void UpdateFlowRefused(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateFlowRefused, reason);
    }

    // outcome: completed (this process now quits for --update-apply) or
    // cancelled (the window closed or the download failed; the app runs on).
    [Event(EvtUpdateFlowFinished,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Update window closed")]
    public void UpdateFlowFinished(string outcome, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateFlowFinished, outcome, duration_ms);
    }

    [Event(EvtUpdateFlowFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The update stopped on an error\nex_type={0} | ex_message={1} | duration_ms={2}")]
    public void UpdateFlowFailed(string ex_type, string ex_message, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtUpdateFlowFailed, ex_type, ex_message, duration_ms);
    }
}
