using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtShutdownRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Shutdown requested | work_id={0}")]
    public void ShutdownRequested(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownRequested, work_id);
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
    // Deckle: the journal must say why. Every restart path ends here when its
    // successor cannot start (settings restart, tray restart, post-build), not
    // only the post-build one the event was first written for.
    [Event(EvtRelaunchFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Cannot relaunch Deckle | {0}: {1}")]
    public void RelaunchFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRelaunchFailed, ex_type, ex_message);
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

    [Event(EvtResidentMutexAbandoned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The previous run ended uncleanly | reason=resident_mutex_abandoned")]
    public void ResidentMutexAbandoned()
    {
        if (IsEnabled()) WriteEvent(EvtResidentMutexAbandoned);
    }

    // The run marker found the previous run still marked in progress: that run
    // never reached its orderly end (a kill, a power loss, a crash). Unlike
    // ResidentMutexAbandoned, it also covers an end the mutex cannot see, and it
    // names the run so its own lines can be found in the file.
    [Event(EvtPreviousRunEndedUncleanly,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The previous run did not end in an orderly way | previous_session={0} | previous_pid={1} | previous_started={2}")]
    public void PreviousRunEndedUncleanly(string previous_session, int previous_pid, string previous_started)
    {
        if (IsEnabled()) WriteEvent(EvtPreviousRunEndedUncleanly, previous_session, previous_pid, previous_started);
    }

    // AppPaths moves the legacy settings layout in its static constructor,
    // before any sink exists, and keeps the failure for this report. The app
    // went on against the new layout, so settings left in the old place read as
    // defaults: a degradation, not a stop.
    [Event(EvtLegacySettingsMigrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The legacy settings layout could not be moved | ex_type={0} | ex_message={1}")]
    public void LegacySettingsMigrationFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLegacySettingsMigrationFailed, ex_type, ex_message);
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
