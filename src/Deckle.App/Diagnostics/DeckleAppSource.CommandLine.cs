using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Command-line ────────────────────────────────────────────────────

    [Event(EvtCmdLineSettingsFlag,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "--settings flag detected | page={0}")]
    public void CmdLineSettingsFlag(string page_tag)
    {
        if (IsEnabled()) WriteEvent(EvtCmdLineSettingsFlag, page_tag);
    }

    [Event(EvtCmdLinePostBuildFlag,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "--post-build flag detected | scheduling shell-execute relaunch in 800ms")]
    public void CmdLinePostBuildFlag()
    {
        if (IsEnabled()) WriteEvent(EvtCmdLinePostBuildFlag);
    }

    [Event(EvtInstallModeEntered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The app was launched in install mode")]
    public void InstallModeEntered()
    {
        if (IsEnabled()) WriteEvent(EvtInstallModeEntered);
    }

    [Event(EvtInstallModeEnteredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "install mode | phase={0} | stub={1} | cleanup={2} | model={3}")]
    public void InstallModeEnteredDetail(string phase, string stub, string cleanup, string model)
    {
        if (IsEnabled()) WriteEvent(EvtInstallModeEnteredDetail, phase, stub, cleanup, model);
    }

}
