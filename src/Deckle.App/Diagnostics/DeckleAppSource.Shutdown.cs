using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtShutdownRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Shutdown requested")]
    public void ShutdownRequested(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownRequested, work_id);
    }

    // One line per failing step, cause included: the log says which
    // teardown broke, not only that one did.
    [Event(EvtShutdownStepFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A shutdown step failed, and shutdown went on\nstep={0} | ex_type={1} | ex_message={2}")]
    public void ShutdownStepFailed(string step, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownStepFailed, step, ex_type, ex_message);
    }

    [Event(EvtRestartRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Restart requested")]
    public void RestartRequested()
    {
        if (IsEnabled()) WriteEvent(EvtRestartRequested);
    }

    [Event(EvtRestartFromTrayRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Restart from tray requested")]
    public void RestartFromTrayRequested()
    {
        if (IsEnabled()) WriteEvent(EvtRestartFromTrayRequested);
    }

    [Event(EvtRestartSpawnNewProcess,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "exe={0} | args={1}")]
    public void RestartSpawnNewProcess(string exe_path, string args)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawnNewProcess, exe_path, args);
    }

    [Event(EvtPostBuildRestartRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Post-build self-restart requested")]
    public void PostBuildRestartRequested()
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRestartRequested);
    }

    [Event(EvtPostBuildShellExecute,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "exe={0}")]
    public void PostBuildShellExecute(string exe_path)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildShellExecute, exe_path);
    }

    // The process exits right after this line, so the user is left with no
    // Deckle: the log must say why. Every restart path ends here when its
    // successor cannot start (settings restart, tray restart, post-build), not
    // only the post-build one the event was first written for.
    [Event(EvtRelaunchFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle could not relaunch itself\nex_type={0} | ex_message={1}")]
    public void RelaunchFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRelaunchFailed, ex_type, ex_message);
    }

    [Event(EvtRestartSpawnSkipped,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Cannot relaunch Deckle\nreason={0}")]
    public void RestartSpawnSkipped(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawnSkipped, reason);
    }

    // Ties the outgoing run to its successor, whose RunIdentity line carries
    // the same pid. child_pid=0 when the shell started it without a handle.
    [Event(EvtRestartSpawned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle started its successor")]
    public void RestartSpawned(int child_pid)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawned, child_pid);
    }

    [Event(EvtRestartSettingsFlushFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Settings could not be saved before the restart\nex_type={0} | ex_message={1}")]
    public void RestartSettingsFlushFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSettingsFlushFailed, ex_type, ex_message);
    }

    [Event(EvtResidentMutexAbandoned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The previous run ended uncleanly")]
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
           Message = "The previous run did not end in an orderly way\nprevious_session={0} | previous_pid={1} | previous_started={2}")]
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
           Message = "The legacy settings layout could not be moved\nex_type={0} | ex_message={1}")]
    public void LegacySettingsMigrationFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLegacySettingsMigrationFailed, ex_type, ex_message);
    }

    // The log and the trace took their current names at this start, on an
    // install that still had the pre-v0.68.1 ones (journal.jsonl, app.jsonl).
    // Said once, so a reader of an older log knows where its history went.
    [Event(EvtLegacyLogFilesRenamed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Log files from an earlier version took their current names")]
    public void LegacyLogFilesRenamed(int files)
    {
        if (IsEnabled()) WriteEvent(EvtLegacyLogFilesRenamed, files);
    }

    // A legacy log file could not take its current name. The new file starts
    // empty beside it and the older history stays under the old name.
    [Event(EvtLegacyLogFileRenameFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A log file from an earlier version kept its old name, so its history stays apart\nfile={0} | renamed={1} | ex_type={2} | ex_message={3}")]
    public void LegacyLogFileRenameFailed(string file, int renamed, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLegacyLogFileRenameFailed, file, renamed, ex_type, ex_message);
    }

    // A second instance is turned away before it may touch the log, so it
    // leaves its fact in second-instance.txt (App.Lifetime.cs) and the resident
    // run copies it here at its next start. noted_at is the time the second
    // instance wrote it, not the time of this line; fact names its pid.
    [Event(EvtSecondInstanceRejected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A second Deckle was turned away")]
    public void SecondInstanceRejected(string noted_at, string fact)
    {
        if (IsEnabled()) WriteEvent(EvtSecondInstanceRejected, noted_at, fact);
    }

    // The same facts when there are too many to read one by one (a launcher
    // shortcut pressed over and over while Deckle runs): how many, and the span.
    [Event(EvtSecondInstancesRejected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Second Deckle instances were turned away")]
    public void SecondInstancesRejected(int count, string first_at, string last_at)
    {
        if (IsEnabled()) WriteEvent(EvtSecondInstancesRejected, count, first_at, last_at);
    }
    [Event(EvtResidentMutexReleaseFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ex_type={0} | ex_message={1}")]
    public void ResidentMutexReleaseFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtResidentMutexReleaseFailed, ex_type, ex_message);
    }

    // One line per swallowed teardown failure that changes nothing for the
    // user: a dispose that threw on the way out.
    [Event(EvtCleanupFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "component={0} | ex_type={1} | ex_message={2}")]
    public void CleanupFailed(string component, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCleanupFailed, component, ex_type, ex_message);
    }
}
