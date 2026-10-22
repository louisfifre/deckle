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
    [Event(EvtDeployStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Placing Deckle | update_mode={0} | app={1} | data={2}")]
    public void DeployStarted(bool update_mode, string app, string data)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployStarted, update_mode, app, data);
    }

    [Event(EvtDeployCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
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
           Message = "Placing Deckle failed | step={0} | ex_type={1} | ex_message={2} | duration_ms={3}")]
    public void DeployFailed(string step, string ex_type, string ex_message, long duration_ms)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployFailed, step, ex_type, ex_message, duration_ms);
    }

    [Event(EvtDeployBlockedByRunningApp,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The install is blocked by a running Deckle | reason=app_running | count={0} | processes={1}")]
    public void DeployBlockedByRunningApp(int count, string processes)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDeployBlockedByRunningApp, count, processes);
    }

}
