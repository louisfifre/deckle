using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Orchestration;

[EventSource(Name = "Deckle-Orchestration")]
public sealed class DeckleOrchestrationSource : DeckleEventSource
{
    public static readonly DeckleOrchestrationSource Log = new();

    private DeckleOrchestrationSource() { }

    public const int EvtSettingsLoadWarning = 1;
    public const int EvtSettingsLoadError = 2;
    public const int EvtEntryAdded = 3;
    public const int EvtEntryAddedDetail = 4;
    public const int EvtEntryRemoved = 5;
    public const int EvtEntryRemovedDetail = 6;
    public const int EvtPageActionFailed = 7;
    public const int EvtPageActionFailedDetail = 8;
    public const int EvtSteamNotFound = 9;
    public const int EvtScanCompleted = 10;
    public const int EvtScanCompletedDetail = 11;
    public const int EvtScanFolderMissing = 12;
    public const int EvtScanFileUnreadable = 13;
    public const int EvtProcessTraceStarted = 14;
    public const int EvtProcessTraceUnavailable = 15;
    public const int EvtProcessTraceUnavailableDetail = 16;
    public const int EvtProcessTraceStopFailed = 17;
    public const int EvtExitWatchUnavailable = 18;
    public const int EvtExitWatchUnavailableDetail = 19;
    public const int EvtProcessTraceEventUnreadable = 20;
    public const int EvtRuntimeStarted = 21;
    public const int EvtRuntimeStopped = 22;
    public const int EvtSessionStarted = 23;
    public const int EvtSessionStartedDetail = 24;
    public const int EvtSessionEnded = 25;
    public const int EvtSessionEndedDetail = 26;
    public const int EvtReconcileDetail = 27;
    public const int EvtProfileApplied = 28;
    public const int EvtProfileAppliedDetail = 29;
    public const int EvtProfileRestored = 30;
    public const int EvtProfileRestoredDetail = 31;
    public const int EvtRuleApplyFailed = 32;
    public const int EvtRuleApplyFailedDetail = 33;
    public const int EvtRuleRestoreFailed = 34;
    public const int EvtRuleRestoreFailedDetail = 35;

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    [Event(EvtEntryAdded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app was added to the registry")]
    public void EntryAdded()
    {
        if (IsEnabled()) WriteEvent(EvtEntryAdded);
    }

    [Event(EvtEntryAddedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "registry entry added | entry_id={0} | display_name={1} | source={2} | process_name={3}")]
    public void EntryAddedDetail(string entry_id, string display_name, string source, string process_name)
    {
        if (IsEnabled()) WriteEvent(EvtEntryAddedDetail, entry_id, display_name, source, process_name);
    }

    [Event(EvtEntryRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app was removed from the registry")]
    public void EntryRemoved()
    {
        if (IsEnabled()) WriteEvent(EvtEntryRemoved);
    }

    [Event(EvtEntryRemovedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "registry entry removed | entry_id={0}")]
    public void EntryRemovedDetail(string entry_id)
    {
        if (IsEnabled()) WriteEvent(EvtEntryRemovedDetail, entry_id);
    }

    // A picker or dialog on the settings page threw. Caught so the page stays
    // usable; the Verbose mirror names the action and the exception.
    [Event(EvtPageActionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An action on the apps and games page failed")]
    public void PageActionFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageActionFailed);
    }

    [Event(EvtPageActionFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page action failed | action={0} | error_type={1} | message={2}")]
    public void PageActionFailedDetail(string action, string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPageActionFailedDetail, action, error_type, message);
    }

    // Neither the SteamPath registry value nor the default install folder led
    // to Steam; the scan goes on with the chosen folders only.
    [Event(EvtSteamNotFound,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "steam not found | checked=registry_steam_path,program_files_x86")]
    public void SteamNotFound()
    {
        if (IsEnabled()) WriteEvent(EvtSteamNotFound);
    }

    [Event(EvtScanCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The scan finished")]
    public void ScanCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtScanCompleted);
    }

    [Event(EvtScanCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "scan completed | steam_found={0} | folder_found={1} | added={2}")]
    public void ScanCompletedDetail(int steam_found, int folder_found, int added)
    {
        if (IsEnabled()) WriteEvent(EvtScanCompletedDetail, steam_found, folder_found, added);
    }

    // A folder the user chose to scan is gone (renamed, unplugged drive). It
    // stays in the list; the next scan tries it again.
    [Event(EvtScanFolderMissing,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "scan folder missing | folder={0}")]
    public void ScanFolderMissing(string folder)
    {
        if (IsEnabled()) WriteEvent(EvtScanFolderMissing, folder);
    }

    // A Steam file or a folder could not be read or parsed during a scan; it
    // is skipped and the scan goes on.
    [Event(EvtScanFileUnreadable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "scan path skipped | path={0} | error_type={1} | message={2}")]
    public void ScanFileUnreadable(string path, string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtScanFileUnreadable, path, error_type, message);
    }

    // The WMI process start subscription is live: apps are noticed the moment
    // they start, not only when they come to the foreground.
    [Event(EvtProcessTraceStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "process trace started | query=Win32_ProcessStartTrace")]
    public void ProcessTraceStarted()
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceStarted);
    }

    // The subscription was refused, normally because Deckle is not elevated.
    // Emitted once per process lifetime; the Verbose mirror follows each refusal.
    [Event(EvtProcessTraceUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Process start events need an elevated Deckle — apps are noticed only when they come to the foreground")]
    public void ProcessTraceUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceUnavailable);
    }

    [Event(EvtProcessTraceUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "process trace unavailable | error_type={0} | message={1}")]
    public void ProcessTraceUnavailableDetail(string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceUnavailableDetail, error_type, message);
    }

    // Stopping the subscription threw; swallowed, the watcher is dropped anyway.
    [Event(EvtProcessTraceStopFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "process trace stop failed | error_type={0} | message={1}")]
    public void ProcessTraceStopFailed(string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceStopFailed, error_type, message);
    }

    // OpenProcess(SYNCHRONIZE) was refused (protected process) or the process
    // was already gone. Emitted once per process lifetime; the Verbose mirror
    // follows each refusal with the pid and the Win32 error.
    [Event(EvtExitWatchUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app cannot be watched for exit — its rules will stay applied until Deckle restarts")]
    public void ExitWatchUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchUnavailable);
    }

    [Event(EvtExitWatchUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "exit watch unavailable | pid={0} | win32_error={1}")]
    public void ExitWatchUnavailableDetail(int pid, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchUnavailableDetail, pid, win32_error);
    }

    // One WMI start event could not be read or handled; it is dropped and the
    // subscription goes on.
    [Event(EvtProcessTraceEventUnreadable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "process start event dropped | error_type={0} | message={1}")]
    public void ProcessTraceEventUnreadable(string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceEventUnreadable, error_type, message);
    }

    [Event(EvtRuntimeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Watching for listed apps")]
    public void RuntimeStarted()
    {
        if (IsEnabled()) WriteEvent(EvtRuntimeStarted);
    }

    [Event(EvtRuntimeStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Stopped watching for listed apps")]
    public void RuntimeStopped()
    {
        if (IsEnabled()) WriteEvent(EvtRuntimeStopped);
    }

    [Event(EvtSessionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app session started")]
    public void SessionStarted()
    {
        if (IsEnabled()) WriteEvent(EvtSessionStarted);
    }

    [Event(EvtSessionStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "app session started | entry_id={0} | display_name={1} | process_name={2} | pid={3}")]
    public void SessionStartedDetail(string entry_id, string display_name, string process_name, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtSessionStartedDetail, entry_id, display_name, process_name, pid);
    }

    [Event(EvtSessionEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app session ended")]
    public void SessionEnded()
    {
        if (IsEnabled()) WriteEvent(EvtSessionEnded);
    }

    [Event(EvtSessionEndedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "app session ended | entry_id={0} | display_name={1}")]
    public void SessionEndedDetail(string entry_id, string display_name)
    {
        if (IsEnabled()) WriteEvent(EvtSessionEndedDetail, entry_id, display_name);
    }

    // One reconcile pass: how many entries are enabled, whether start events
    // flow, and how many processes the one-shot enumeration offered the tracker.
    [Event(EvtReconcileDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reconcile | enabled_entries={0} | process_trace={1} | processes_seen={2}")]
    public void ReconcileDetail(int enabled_entries, bool process_trace, int processes_seen)
    {
        if (IsEnabled()) WriteEvent(EvtReconcileDetail, enabled_entries, process_trace, processes_seen);
    }

    // A session's rules were applied at launch. effects = the effect names that
    // applied, comma-separated; changes = how many things a restore will undo
    // (0 when everything was already as wanted).
    [Event(EvtProfileApplied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "App rules applied")]
    public void ProfileApplied()
    {
        if (IsEnabled()) WriteEvent(EvtProfileApplied);
    }

    [Event(EvtProfileAppliedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "app rules applied | entry_id={0} | effects={1} | changes={2}")]
    public void ProfileAppliedDetail(string entry_id, string effects, int changes)
    {
        if (IsEnabled()) WriteEvent(EvtProfileAppliedDetail, entry_id, effects, changes);
    }

    [Event(EvtProfileRestored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "App rules restored")]
    public void ProfileRestored()
    {
        if (IsEnabled()) WriteEvent(EvtProfileRestored);
    }

    [Event(EvtProfileRestoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "app rules restored | entry_id={0} | restored={1}")]
    public void ProfileRestoredDetail(string entry_id, int restored)
    {
        if (IsEnabled()) WriteEvent(EvtProfileRestoredDetail, entry_id, restored);
    }

    // One effect threw while applying; the effects applied before it were
    // undone and the session runs without rules.
    [Event(EvtRuleApplyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "App rules could not be applied — the previous state was restored")]
    public void RuleApplyFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRuleApplyFailed);
    }

    [Event(EvtRuleApplyFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "rule apply failed | entry_id={0} | effect={1} | error_type={2} | message={3}")]
    public void RuleApplyFailedDetail(string entry_id, string effect, string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRuleApplyFailedDetail, entry_id, effect, error_type, message);
    }

    // One undo threw; the other undos still ran. What it changed stays changed.
    [Event(EvtRuleRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A rule could not be restored")]
    public void RuleRestoreFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRuleRestoreFailed);
    }

    [Event(EvtRuleRestoreFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "rule restore failed | entry_id={0} | effect={1} | error_type={2} | message={3}")]
    public void RuleRestoreFailedDetail(string entry_id, string effect, string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRuleRestoreFailedDetail, entry_id, effect, error_type, message);
    }
}
