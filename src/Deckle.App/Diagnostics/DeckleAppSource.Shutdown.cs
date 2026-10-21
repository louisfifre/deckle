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

    // The process exits right after this line, so the user is left with no
    // Deckle: the journal must say why.
    [Event(EvtPostBuildRelaunchFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Cannot relaunch Deckle | {0}: {1}")]
    public void PostBuildRelaunchFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRelaunchFailed, ex_type, ex_message);
    }

    [Event(EvtRestartSpawnSkipped,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Cannot relaunch Deckle | reason={0}")]
    public void RestartSpawnSkipped(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawnSkipped, reason);
    }

    // Ties the outgoing run to its successor, whose RunIdentity line carries
    // the same pid. child_pid=0 when the shell started it without a handle.
    [Event(EvtRestartSpawned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Deckle started its successor | child_pid={0}")]
    public void RestartSpawned(int child_pid)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawned, child_pid);
    }

    [Event(EvtRestartSettingsFlushFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Settings could not be saved before the restart | {0}: {1}")]
    public void RestartSettingsFlushFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSettingsFlushFailed, ex_type, ex_message);
    }

    [Event(EvtSecondInstanceRejected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Another Deckle is resident")]
    public void SecondInstanceRejected()
    {
        if (IsEnabled()) WriteEvent(EvtSecondInstanceRejected);
    }

    [Event(EvtResidentMutexAbandoned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The previous run ended uncleanly | reason=resident_mutex_abandoned")]
    public void ResidentMutexAbandoned()
    {
        if (IsEnabled()) WriteEvent(EvtResidentMutexAbandoned);
    }

    [Event(EvtResidentMutexReleaseFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "resident mutex release failed | ex_type={0} | ex_message={1}")]
    public void ResidentMutexReleaseFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtResidentMutexReleaseFailed, ex_type, ex_message);
    }

    // One line per swallowed teardown failure that changes nothing for the
    // user: a dispose that threw on the way out.
    [Event(EvtCleanupFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cleanup failed | component={0} | ex_type={1} | ex_message={2}")]
    public void CleanupFailed(string component, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCleanupFailed, component, ex_type, ex_message);
    }
}
