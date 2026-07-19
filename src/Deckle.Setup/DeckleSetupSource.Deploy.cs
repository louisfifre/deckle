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

}
