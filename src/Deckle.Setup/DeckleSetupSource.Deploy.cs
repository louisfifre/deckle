using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
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

    // Deploy is the install or update pipeline of this run: its start and its
    // completion go to the journal so the run's story says it was attempted.
    // Start, completion, failure and block carry the work_id of one attempt (a
    // retry is a new attempt), so a start with no end reads as abandoned.
    [Event(EvtDeployStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Placing Deckle | work_id={0} | update_mode={1} | app={2} | data={3}")]
    public void DeployStarted(string work_id, bool update_mode, string app, string data)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployStarted, work_id, update_mode, app, data);
    }

    [Event(EvtDeployCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Deckle was placed and registered | work_id={0}")]
    public void DeployCompleted(string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployCompleted, work_id);
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
           Message = "Placing Deckle failed | work_id={0} | step={1} | ex_type={2} | ex_message={3} | duration_ms={4}")]
    public void DeployFailed(string work_id, string step, string ex_type, string ex_message, long duration_ms)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployFailed, work_id, step, ex_type, ex_message, duration_ms);
    }

    // The attempt ends here with nothing placed, which is how work that does
    // not happen reads in the journal: tagged like a start, with its reason.
    [Event(EvtDeployBlockedByRunningApp,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The install is blocked by a running Deckle | work_id={0} | reason=app_running | count={1} | processes={2}")]
    public void DeployBlockedByRunningApp(string work_id, int count, string processes)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployBlockedByRunningApp, work_id, count, processes);
    }

}
