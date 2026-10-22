using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Orchestration;

// Keywords. Lifecycle: the module's settings, the runtime and the process
// start subscription coming and going. Capture: detection, the sources that
// feed sessions (process starts, foreground, exits) and the sessions
// themselves. Push: what a session changes in the world (rules, HDR).
// Windowing: the monitor list. Pipeline: the Steam and folder scan.
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
    // 7 retired.
    // 8 retired.
    public const int EvtSteamNotFound = 9;
    public const int EvtScanCompleted = 10;
    public const int EvtScanCompletedDetail = 11;
    public const int EvtScanFolderMissing = 12;
    // 13 retired.
    public const int EvtProcessTraceStarted = 14;
    // 15 retired.
    // 16 retired.
    // 17 retired.
    // 18 retired.
    public const int EvtExitWatchUnavailableDetail = 19;
    // 20 retired.
    // 21 retired.
    public const int EvtRuntimeStopped = 22;
    public const int EvtSessionStarted = 23;
    public const int EvtSessionStartedDetail = 24;
    // 25 retired.
    public const int EvtSessionEndedDetail = 26;
    public const int EvtReconcileDetail = 27;
    public const int EvtProfileApplied = 28;
    public const int EvtProfileAppliedDetail = 29;
    public const int EvtProfileRestored = 30;
    // 31 retired.
    // 32 retired.
    // 33 retired.
    // 34 retired.
    // 35 retired.
    public const int EvtDisplayQueryFailed = 36;
    // 37 retired.
    // 38 retired.
    // 39 retired.
    public const int EvtHdrRuleSkipped = 40;
    public const int EvtForegroundPathUnavailable = 41;
    // 42 retired.
    public const int EvtPageActionFailed = 43;
    public const int EvtScanPathSkipped = 44;
    public const int EvtProcessTraceUnavailable = 45;
    public const int EvtProcessTraceUnavailableDetail = 46;
    public const int EvtProcessTraceStopFailed = 47;
    public const int EvtExitWatchUnavailable = 48;
    public const int EvtProcessTraceEventDropped = 49;
    public const int EvtProcessTraceEventDroppedDetail = 50;
    public const int EvtRuntimeStarted = 51;
    public const int EvtSessionEnded = 52;
    public const int EvtProfileRestoredDetail = 53;
    public const int EvtRuleApplyFailed = 54;
    public const int EvtRuleRestoreFailed = 55;
    public const int EvtHdrStateChanged = 56;
    public const int EvtHdrSetFailed = 57;
    public const int EvtForegroundDeliveryFailed = 58;
    public const int EvtProcessTraceStopped = 59;
    public const int EvtProcessTraceLost = 60;
    public const int EvtDetectionStatusChanged = 61;
    public const int EvtReconcileFailed = 62;
    public const int EvtReconcileFailedDetail = 63;
    public const int EvtReconcileRecovered = 64;
    public const int EvtExitWatchCallbackFailed = 65;
    public const int EvtSessionHandlerFailed = 66;
    public const int EvtDisplayQueryFailedDetail = 67;
    public const int EvtDisplayQueryRecovered = 68;
    public const int EvtScanStarted = 69;
    public const int EvtScanFailed = 70;
    public const int EvtSteamLibraryScanned = 71;
    public const int EvtSteamPathUnreadable = 72;
    public const int EvtProcessQuerySkipped = 73;
    public const int EvtForegroundDeliveryFailedDetail = 74;
    public const int EvtForegroundDeliveryRecovered = 75;
    public const int EvtExitWatchCallbackFailedDetail = 76;
    public const int EvtExitWatchCallbackRecovered = 77;

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The apps and games settings need attention | {0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The apps and games settings could not be saved | {0}")]
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
    // usable.
    [Event(EvtPageActionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An action on the apps and games page failed | action={0} | ex_type={1} | ex_message={2}")]
    public void PageActionFailed(string action, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageActionFailed, action, ex_type, ex_message);
    }

    // Neither the SteamPath registry value nor the default install folder led
    // to Steam; the scan goes on with the chosen folders only.
    [Event(EvtSteamNotFound,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "steam not found | checked=registry_steam_path,program_files_x86")]
    public void SteamNotFound()
    {
        if (IsEnabled()) WriteEvent(EvtSteamNotFound);
    }

    // The SteamPath registry value could not be read; the default install
    // folder is tried next.
    [Event(EvtSteamPathUnreadable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "steam path unreadable | source={0} | ex_type={1} | ex_message={2}")]
    public void SteamPathUnreadable(string source, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSteamPathUnreadable, source, ex_type, ex_message);
    }

    [Event(EvtScanStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Scanning Steam and the chosen folders | scan_folders={0}")]
    public void ScanStarted(int scan_folders)
    {
        if (IsEnabled()) WriteEvent(EvtScanStarted, scan_folders);
    }

    [Event(EvtScanCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The scan finished")]
    public void ScanCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtScanCompleted);
    }

    [Event(EvtScanCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "scan completed | steam_found={0} | folder_found={1} | added={2}")]
    public void ScanCompletedDetail(int steam_found, int folder_found, int added)
    {
        if (IsEnabled()) WriteEvent(EvtScanCompletedDetail, steam_found, folder_found, added);
    }

    // The scan threw past its per-path guards. It closes ScanStarted; Verbose
    // because the page's PageActionFailed (action=scan) is the Warning for the
    // same failure, and one cause should raise one Warning.
    [Event(EvtScanFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "scan failed | ex_type={0} | ex_message={1}")]
    public void ScanFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtScanFailed, ex_type, ex_message);
    }

    // One Steam library: manifests kept, and skipped ones (unreadable, no
    // game folder on disk, incomplete, or the shared redistributables).
    [Event(EvtSteamLibraryScanned,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "steam library scanned | library={0} | manifests={1} | skipped={2}")]
    public void SteamLibraryScanned(string library, int manifests, int skipped)
    {
        if (IsEnabled()) WriteEvent(EvtSteamLibraryScanned, library, manifests, skipped);
    }

    // A folder the scan expected is gone (renamed, unplugged drive): one the
    // user chose, or a Steam library's steamapps. It stays in the list; the
    // next scan tries it again.
    [Event(EvtScanFolderMissing,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "scan folder missing | folder={0}")]
    public void ScanFolderMissing(string folder)
    {
        if (IsEnabled()) WriteEvent(EvtScanFolderMissing, folder);
    }

    // A Steam file or a folder could not be read or parsed during a scan; it
    // is skipped and the scan goes on.
    [Event(EvtScanPathSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "scan path skipped | path={0} | ex_type={1} | ex_message={2}")]
    public void ScanPathSkipped(string path, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtScanPathSkipped, path, ex_type, ex_message);
    }

    // The WMI process start subscription is live: apps are noticed the moment
    // they start, not only when they come to the foreground.
    [Event(EvtProcessTraceStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Process start events are on")]
    public void ProcessTraceStarted()
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceStarted);
    }

    // Stopped on purpose: no entry is enabled any more, or the runtime stops.
    [Event(EvtProcessTraceStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Process start events are off")]
    public void ProcessTraceStopped()
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceStopped);
    }

    // WMI ended a live subscription on its own (service restart, quota). It is
    // not resubscribed; the detection falls back on the foreground until the
    // enabled entries drop to none and come back, or Deckle restarts.
    [Event(EvtProcessTraceLost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Process start events stopped — apps are noticed only when they come to the foreground | status={0}")]
    public void ProcessTraceLost(string status)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceLost, status);
    }

    // The subscription was refused, normally because Deckle is not elevated.
    // The runtime does not ask again while Deckle runs. Emitted once per
    // process lifetime; a later refusal only gets the Verbose detail.
    [Event(EvtProcessTraceUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Process start events are unavailable until Deckle restarts — apps are noticed only when they come to the foreground | elevated={0} | ex_type={1} | ex_message={2} | hr={3}")]
    public void ProcessTraceUnavailable(bool elevated, string ex_type, string ex_message, int hr)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceUnavailable, elevated, ex_type, ex_message, hr);
    }

    [Event(EvtProcessTraceUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "process trace unavailable | ex_type={0} | ex_message={1} | hr={2}")]
    public void ProcessTraceUnavailableDetail(string ex_type, string ex_message, int hr)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceUnavailableDetail, ex_type, ex_message, hr);
    }

    // Stopping the subscription threw; swallowed, the watcher is dropped anyway.
    [Event(EvtProcessTraceStopFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "process trace stop failed | ex_type={0} | ex_message={1}")]
    public void ProcessTraceStopFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceStopFailed, ex_type, ex_message);
    }

    // OpenProcess(SYNCHRONIZE) was refused (protected process). Emitted once
    // per process lifetime; a later refusal only gets the Verbose detail.
    [Event(EvtExitWatchUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "An app cannot be watched for exit — its rules will stay applied until Deckle restarts | pid={0} | win32_error={1}")]
    public void ExitWatchUnavailable(int pid, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchUnavailable, pid, win32_error);
    }

    [Event(EvtExitWatchUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "exit watch unavailable | pid={0} | win32_error={1}")]
    public void ExitWatchUnavailableDetail(int pid, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchUnavailableDetail, pid, win32_error);
    }

    // The exit callback threw on the thread pool. Contained so the resident
    // process lives on; the session of that process may not end, and its
    // rules then stay applied until the runtime stops. Opens one incident:
    // later failures only get the Verbose detail, and the next exit that is
    // handled closes it.
    [Event(EvtExitWatchCallbackFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A process exit could not be handled — its app's rules may stay applied | pid={0} | ex_type={1} | ex_message={2}")]
    public void ExitWatchCallbackFailed(int pid, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchCallbackFailed, pid, ex_type, ex_message);
    }

    [Event(EvtExitWatchCallbackFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "process exit not handled | pid={0} | ex_type={1} | ex_message={2}")]
    public void ExitWatchCallbackFailedDetail(int pid, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchCallbackFailedDetail, pid, ex_type, ex_message);
    }

    [Event(EvtExitWatchCallbackRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "Process exits are handled again")]
    public void ExitWatchCallbackRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtExitWatchCallbackRecovered);
    }

    // One WMI start event could not be read (stage=read) or handled
    // (stage=deliver); it is dropped and the subscription goes on. The
    // process is still noticed if it comes to the foreground. Once per process
    // lifetime; later drops only get the Verbose detail.
    [Event(EvtProcessTraceEventDropped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A process start event was dropped | stage={0} | ex_type={1} | ex_message={2}")]
    public void ProcessTraceEventDropped(string stage, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceEventDropped, stage, ex_type, ex_message);
    }

    [Event(EvtProcessTraceEventDroppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "process start event dropped | stage={0} | ex_type={1} | ex_message={2}")]
    public void ProcessTraceEventDroppedDetail(string stage, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessTraceEventDroppedDetail, stage, ex_type, ex_message);
    }

    // A process exited or refused a query between being listed and being
    // read; it is not offered. stage = reconcile or running_apps.
    [Event(EvtProcessQuerySkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "process skipped | stage={0} | pid={1} | ex_type={2} | ex_message={3}")]
    public void ProcessQuerySkipped(string stage, int pid, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtProcessQuerySkipped, stage, pid, ex_type, ex_message);
    }

    // The detection mode at start: elevated predicts whether process start
    // events will flow, foreground_monitor whether apps are noticed when they
    // come to the front.
    [Event(EvtRuntimeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Watching for listed apps | enabled_entries={0} | elevated={1} | foreground_monitor={2}")]
    public void RuntimeStarted(int enabled_entries, bool elevated, bool foreground_monitor)
    {
        if (IsEnabled()) WriteEvent(EvtRuntimeStarted, enabled_entries, elevated, foreground_monitor);
    }

    [Event(EvtRuntimeStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Stopped watching for listed apps")]
    public void RuntimeStopped()
    {
        if (IsEnabled()) WriteEvent(EvtRuntimeStopped);
    }

    // What the settings page reads to advise "Start elevated" changed.
    [Event(EvtDetectionStatusChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "detection status changed | process_trace={0} | foreground_monitor={1}")]
    public void DetectionStatusChanged(bool process_trace, bool foreground_monitor)
    {
        if (IsEnabled()) WriteEvent(EvtDetectionStatusChanged, process_trace, foreground_monitor);
    }

    // A session is lasting work: SessionStarted opens it and SessionEnded
    // closes it, both in the journal. entry_id is the registry entry's opaque
    // identifier and pairs them, since an entry has one session at a time; the
    // app's name and process stay in the Verbose details.
    [Event(EvtSessionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "An app session started | entry_id={0} | pid={1}")]
    public void SessionStarted(string entry_id, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtSessionStarted, entry_id, pid);
    }

    [Event(EvtSessionStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "app session started | entry_id={0} | display_name={1} | process_name={2} | pid={3}")]
    public void SessionStartedDetail(string entry_id, string display_name, string process_name, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtSessionStartedDetail, entry_id, display_name, process_name, pid);
    }

    // reason = exited (its last process ended), entry_removed, entry_disabled,
    // or shutdown (the runtime stopped).
    [Event(EvtSessionEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "An app session ended | entry_id={0} | reason={1}")]
    public void SessionEnded(string entry_id, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtSessionEnded, entry_id, reason);
    }

    [Event(EvtSessionEndedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "app session ended | entry_id={0} | display_name={1}")]
    public void SessionEndedDetail(string entry_id, string display_name)
    {
        if (IsEnabled()) WriteEvent(EvtSessionEndedDetail, entry_id, display_name);
    }

    // A handler of a session event threw. Contained so the events queued
    // behind it are still delivered; kind = process_tracked, session_started
    // or session_ended.
    [Event(EvtSessionHandlerFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "An app session event could not be handled | kind={0} | entry_id={1} | ex_type={2} | ex_message={3}")]
    public void SessionHandlerFailed(string kind, string entry_id, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSessionHandlerFailed, kind, entry_id, ex_type, ex_message);
    }

    // One reconcile pass: how many entries are enabled, whether start events
    // flow, and how many processes the one-shot enumeration offered the tracker.
    // One per settings change burst: low rate, so no admission gate.
    [Event(EvtReconcileDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "reconcile | enabled_entries={0} | process_trace={1} | processes_seen={2}")]
    public void ReconcileDetail(int enabled_entries, bool process_trace, int processes_seen)
    {
        if (IsEnabled()) WriteEvent(EvtReconcileDetail, enabled_entries, process_trace, processes_seen);
    }

    // A reconcile pass threw on the thread pool. Contained so the resident
    // process lives on; detection may lag the app list until a pass succeeds.
    // Opens one incident; later failures only get the Verbose detail, and the
    // next pass that succeeds closes it. site is the Type.Method that threw,
    // in place of a stack trace, which could carry build paths.
    [Event(EvtReconcileFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Detection could not catch up with the app list | ex_type={0} | ex_message={1} | site={2}")]
    public void ReconcileFailed(string ex_type, string ex_message, string site)
    {
        if (IsEnabled()) WriteEvent(EvtReconcileFailed, ex_type, ex_message, site);
    }

    [Event(EvtReconcileFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "reconcile failed | ex_type={0} | ex_message={1} | site={2}")]
    public void ReconcileFailedDetail(string ex_type, string ex_message, string site)
    {
        if (IsEnabled()) WriteEvent(EvtReconcileFailedDetail, ex_type, ex_message, site);
    }

    [Event(EvtReconcileRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "Detection caught up with the app list again")]
    public void ReconcileRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtReconcileRecovered);
    }

    // A session's rules changed something at launch. Not emitted when every
    // rule found the system already as wanted: the detail alone says so.
    // changes = how many things a restore will undo.
    [Event(EvtProfileApplied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
           Message = "App rules applied | entry_id={0} | changes={1}")]
    public void ProfileApplied(string entry_id, int changes)
    {
        if (IsEnabled()) WriteEvent(EvtProfileApplied, entry_id, changes);
    }

    // effects = the effect names that applied, comma-separated; changes = how
    // many things a restore will undo (0 when everything was already as wanted).
    [Event(EvtProfileAppliedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "app rules applied | entry_id={0} | effects={1} | changes={2}")]
    public void ProfileAppliedDetail(string entry_id, string effects, int changes)
    {
        if (IsEnabled()) WriteEvent(EvtProfileAppliedDetail, entry_id, effects, changes);
    }

    // Every change was undone. When one undo failed, RuleRestoreFailed says
    // so and this milestone is not emitted.
    [Event(EvtProfileRestored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
           Message = "App rules restored | entry_id={0} | restored={1}")]
    public void ProfileRestored(string entry_id, int restored)
    {
        if (IsEnabled()) WriteEvent(EvtProfileRestored, entry_id, restored);
    }

    [Event(EvtProfileRestoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "app rules restored | entry_id={0} | restored={1} | total={2}")]
    public void ProfileRestoredDetail(string entry_id, int restored, int total)
    {
        if (IsEnabled()) WriteEvent(EvtProfileRestoredDetail, entry_id, restored, total);
    }

    // One effect threw while applying; the effects applied before it were
    // undone and the session runs without rules.
    [Event(EvtRuleApplyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "App rules could not be applied — the app runs without them | entry_id={0} | effect={1} | ex_type={2} | ex_message={3}")]
    public void RuleApplyFailed(string entry_id, string effect, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRuleApplyFailed, entry_id, effect, ex_type, ex_message);
    }

    // One undo threw; the other undos still ran. What it changed stays changed.
    [Event(EvtRuleRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A rule could not be restored | entry_id={0} | effect={1} | ex_type={2} | ex_message={3}")]
    public void RuleRestoreFailed(string entry_id, string effect, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRuleRestoreFailed, entry_id, effect, ex_type, ex_message);
    }

    // QueryDisplayConfig failed; the monitor list reads as empty, so no HDR
    // row shows and every HDR rule skips with reason=display_query_failed.
    // Opens one incident; later failures only get the Verbose detail, and the
    // next successful query closes it.
    [Event(EvtDisplayQueryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "The monitor list could not be read — HDR rules are skipped | win32_error={0}")]
    public void DisplayQueryFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtDisplayQueryFailed, win32_error);
    }

    [Event(EvtDisplayQueryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "display query failed | win32_error={0}")]
    public void DisplayQueryFailedDetail(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtDisplayQueryFailedDetail, win32_error);
    }

    [Event(EvtDisplayQueryRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Windowing,
           Tags = ObservationTags.Journal,
           Message = "The monitor list can be read again")]
    public void DisplayQueryRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtDisplayQueryRecovered);
    }

    // api = hdr_state (the dedicated pair of build 26100 and later) or
    // advanced_color (the older pair, which also toggles wide color gamut).
    [Event(EvtHdrStateChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "hdr switched | device={0} | old_state={1} | new_state={2} | api={3}")]
    public void HdrStateChanged(string device, bool old_state, bool new_state, string api)
    {
        if (IsEnabled()) WriteEvent(EvtHdrStateChanged, device, old_state, new_state, api);
    }

    [Event(EvtHdrSetFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "HDR could not be switched on a monitor | device={0} | win32_error={1} | api={2}")]
    public void HdrSetFailed(string device, int win32_error, string api)
    {
        if (IsEnabled()) WriteEvent(EvtHdrSetFailed, device, win32_error, api);
    }

    // A monitor named in a rule was left alone: reason = absent (not
    // connected now), not_hdr_capable, unreadable (its state could not be
    // read), or display_query_failed (the monitor list itself could not be read).
    [Event(EvtHdrRuleSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "hdr rule skipped | device={0} | reason={1}")]
    public void HdrRuleSkipped(string device, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtHdrRuleSkipped, device, reason);
    }

    // The foreground window's process refused the image path query
    // (protected process) or exited first. The change is still raised with
    // a null path, so the entry can match by process name. Once per pid.
    [Event(EvtForegroundPathUnavailable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "foreground image path unavailable | pid={0} | process_name={1}")]
    public void ForegroundPathUnavailable(int pid, string process_name)
    {
        if (IsEnabled()) WriteEvent(EvtForegroundPathUnavailable, pid, process_name);
    }

    // A subscriber of the foreground change threw on the thread pool. The
    // exception is contained here; the change is dropped for that delivery.
    // Opens one incident: a subscriber that fails on every change would
    // otherwise write a Warning per focus change; later failures only get the
    // Verbose detail, and the next delivery that succeeds closes it.
    [Event(EvtForegroundDeliveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A foreground change could not be delivered | pid={0} | ex_type={1} | ex_message={2}")]
    public void ForegroundDeliveryFailed(int pid, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtForegroundDeliveryFailed, pid, ex_type, ex_message);
    }

    [Event(EvtForegroundDeliveryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "foreground change not delivered | pid={0} | ex_type={1} | ex_message={2}")]
    public void ForegroundDeliveryFailedDetail(int pid, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtForegroundDeliveryFailedDetail, pid, ex_type, ex_message);
    }

    [Event(EvtForegroundDeliveryRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "Foreground changes are delivered again")]
    public void ForegroundDeliveryRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtForegroundDeliveryRecovered);
    }
}
