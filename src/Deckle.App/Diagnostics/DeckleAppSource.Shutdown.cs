using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtShutdownRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Shutdown requested")]
    public void ShutdownRequested()
    {
        if (IsEnabled()) WriteEvent(EvtShutdownRequested);
    }

    [Event(EvtShutdownWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A shutdown step failed")]
    public void ShutdownWarning()
    {
        if (IsEnabled()) WriteEvent(EvtShutdownWarning);
    }

    [Event(EvtShutdownWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shutdown step failed | message={0}")]
    public void ShutdownWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownWarningDetail, message);
    }

    // One line per failing step, cause included: the journal says which
    // teardown broke, not only that one did.
    [Event(EvtShutdownStepFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Shutdown step {0} failed | {1}: {2}")]
    public void ShutdownStepFailed(string step, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownStepFailed, step, ex_type, ex_message);
    }

    [Event(EvtShutdownStepsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shutdown steps | steps={0} | failed={1} | elapsed_ms={2}")]
    public void ShutdownStepsDetail(int steps, int failed, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownStepsDetail, steps, failed, elapsed_ms);
    }

    [Event(EvtRestartRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Restart requested")]
    public void RestartRequested()
    {
        if (IsEnabled()) WriteEvent(EvtRestartRequested);
    }

    [Event(EvtRestartFromTrayRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Restart from tray requested")]
    public void RestartFromTrayRequested()
    {
        if (IsEnabled()) WriteEvent(EvtRestartFromTrayRequested);
    }

    [Event(EvtRestartSpawnNewProcess,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "spawn new process | exe={0} | args={1}")]
    public void RestartSpawnNewProcess(string exe_path, string args)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawnNewProcess, exe_path, args);
    }

    [Event(EvtPostBuildRestartRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Post-build self-restart requested")]
    public void PostBuildRestartRequested()
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRestartRequested);
    }

    [Event(EvtPostBuildShellExecute,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shell-execute relaunch | exe={0}")]
    public void PostBuildShellExecute(string exe_path)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildShellExecute, exe_path);
    }

    [Event(EvtPostBuildRelaunchFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not relaunch after build")]
    public void PostBuildRelaunchFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRelaunchFailed);
    }

    [Event(EvtPostBuildRelaunchFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shell-execute relaunch failed | message={0}")]
    public void PostBuildRelaunchFailedDetail(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRelaunchFailedDetail, ex_message);
    }

}
