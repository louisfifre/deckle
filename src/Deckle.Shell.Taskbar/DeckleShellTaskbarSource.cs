using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.Taskbar;

// Taskbar module provider. Two pieces write here: the tray icon promoter (and
// its registry watcher) and the native engine host. Counts and codes
// only: an icon's name, path or publisher never reaches a line, since they
// tell what the person runs.
[EventSource(Name = "Deckle-Taskbar")]
public sealed class DeckleShellTaskbarSource : DeckleEventSource
{
    public static readonly DeckleShellTaskbarSource Log = new();

    private DeckleShellTaskbarSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Sequential from 1. IDs are public in the ETW manifest; do not reuse an
    // ID after deleting an event.
    public const int EvtTrayPromotionStarted         = 1;
    public const int EvtTrayPromotionStartedDetail   = 2;
    public const int EvtTrayPromotionStopped         = 3;
    public const int EvtTrayPromotionStoppedDetail   = 4;
    public const int EvtTrayIconsPromotedDetail      = 5;
    public const int EvtTrayPassFailed               = 6;
    public const int EvtTrayPassRecovered            = 7;
    public const int EvtTrayWatcherStateChanged      = 8;
    public const int EvtTrayWatcherFailed            = 9;
    public const int EvtTraySnapshotUnreadable       = 10;
    // 11 to 14 belonged to the Recommended section policy, removed; not reused.
    public const int EvtSettingsLoadWarning          = 15;
    public const int EvtSettingsLoadError            = 16;
    public const int EvtExplorerRestartReapplyFailed = 17;
    public const int EvtNativeEngineInjected         = 18;
    public const int EvtNativeEngineConnected        = 19;
    public const int EvtNativeEngineDisconnected     = 20;
    public const int EvtNativeEngineUnavailable      = 21;
    public const int EvtNativeEngineNote             = 22;
    public const int EvtNativeEngineDetail           = 23;
    public const int EvtNativeEngineWarning          = 24;
    public const int EvtNativeEngineError            = 25;
    public const int EvtTaskbarPartsChanged          = 26;
    public const int EvtTaskbarPartSkipped           = 27;
    public const int EvtTaskbarPartNotInEffect       = 28;
    public const int EvtTaskbarPartProblemEnded      = 29;
    public const int EvtTaskbarEngineUnloadUnconfirmed = 30;
    public const int EvtTaskbarCustomizerFailed      = 31;

    // ── Tray icon promotion ───────────────────────────────────────────────────
    //
    // Started and Stopped bracket the time the promoter holds the icons on the
    // bar: Started when the first Apply of a run takes hold, Stopped when a
    // Restore gives the icons back. Quitting Deckle restores nothing, so a log
    // that ends on Started and then the shutdown milestone is the normal case.

    [Event(EvtTrayPromotionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Showing every tray icon on the taskbar")]
    public void TrayPromotionStarted()
    {
        if (IsEnabled()) WriteEvent(EvtTrayPromotionStarted);
    }

    // trigger: setting (the page turned it on), startup or explorer_restart.
    // snapshot: taken (this run recorded what the icons held) or kept (an
    // earlier run's record is still the one to give back).
    [Event(EvtTrayPromotionStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "trigger={0} | icons={1} | promoted={2} | snapshot={3}")]
    public void TrayPromotionStartedDetail(string trigger, int icons, int promoted, string snapshot)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTrayPromotionStartedDetail, trigger, icons, promoted, snapshot);
    }

    [Event(EvtTrayPromotionStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Gave the tray icons back to Windows")]
    public void TrayPromotionStopped()
    {
        if (IsEnabled()) WriteEvent(EvtTrayPromotionStopped);
    }

    [Event(EvtTrayPromotionStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "written={0} | icons={1}")]
    public void TrayPromotionStoppedDetail(int written, int icons)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTrayPromotionStoppedDetail, written, icons);
    }

    // A pass that wrote something after the first one (the watcher's, or an
    // Apply on a running promoter). A pass that writes nothing logs nothing, so
    // an idle key, Deckle's own write included, leaves no trace.
    [Event(EvtTrayIconsPromotedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "trigger={0} | promoted={1} | icons={2}")]
    public void TrayIconsPromotedDetail(string trigger, int promoted, int icons)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTrayIconsPromotedDetail, trigger, promoted, icons);
    }

    // One Warning per incident: a run of failed passes opens it, the first
    // pass that goes through closes it (TrayPassRecovered). trigger: setting,
    // startup, explorer_restart, watcher or restore. error is an exception type,
    // key-missing or watcher-stopped.
    [Event(EvtTrayPassFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray icons could not be brought in line with the setting, so they may not match it\ntrigger={0} | error={1}")]
    public void TrayPassFailed(string trigger, string error)
    {
        if (IsEnabled()) WriteEvent(EvtTrayPassFailed, trigger, error);
    }

    [Event(EvtTrayPassRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The tray icons match the setting again")]
    public void TrayPassRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtTrayPassRecovered);
    }

    // state: started, stopped, restarting or callback_failed:<exception type>.
    [Event(EvtTrayWatcherStateChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state={0}")]
    public void TrayWatcherStateChanged(string state)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTrayWatcherStateChanged, state);
    }

    // step: open (error key-missing), arm (the Win32 error code) or watch (an
    // exception type). Written once per loss: the promoter restarts a lost
    // watcher once, and a restart that fails again ends the chain.
    [Event(EvtTrayWatcherFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Stopped watching for new tray icons, so icons that appear later may land in the overflow\nstep={0} | error={1}")]
    public void TrayWatcherFailed(string step, string error)
    {
        if (IsEnabled()) WriteEvent(EvtTrayWatcherFailed, step, error);
    }

    // backup: kept (the unreadable file was copied to .bak) or an exception
    // type when even the copy failed.
    [Event(EvtTraySnapshotUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray icon snapshot could not be read, so what the icons held before cannot be given back\nex_type={0} | backup={1}")]
    public void TraySnapshotUnreadable(string ex_type, string backup)
    {
        if (IsEnabled()) WriteEvent(EvtTraySnapshotUnreadable, ex_type, backup);
    }

    // The App relays an Explorer restart to the promoter inside the tray
    // window's message handler, where an exception must never escape.
    [Event(EvtExplorerRestartReapplyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray icons could not be kept on the taskbar after Explorer restarted\nex_type={0} | ex_message={1}")]
    public void ExplorerRestartReapplyFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtExplorerRestartReapplyFailed, ex_type, ex_message);
    }

    // ── Settings store ────────────────────────────────────────────────────────

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // ── Native engine (the DLL inside explorer) ───────────────────────────────
    //
    // The DLL cannot write to Deckle's log: it sends its records over the pipe
    // and NativeLogRelay re-emits them here, at the level the DLL gave them.
    // Injected and Disconnected bracket one DLL's stay in explorer, Connected
    // sits between them; the explorer process id pairs a stay in the file alone.

    // trigger: startup, explorer_restart or retry.
    [Event(EvtNativeEngineInjected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Sent the taskbar engine into explorer")]
    public void NativeEngineInjected(string trigger)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineInjected, trigger);
    }

    [Event(EvtNativeEngineConnected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The taskbar engine connected from explorer")]
    public void NativeEngineConnected(int explorer_pid, int protocol)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineConnected, explorer_pid, protocol);
    }

    // outcome: unloaded (the DLL reported that it unloaded itself) or lost (the
    // pipe closed with no word, as when explorer exits).
    [Event(EvtNativeEngineDisconnected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The taskbar engine disconnected")]
    public void NativeEngineDisconnected(string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineDisconnected, outcome);
    }

    // step: the injection step that failed. error: its Win32 error code.
    [Event(EvtNativeEngineUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar engine could not be started\nstep={0} | error={1}")]
    public void NativeEngineUnavailable(string step, int error)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineUnavailable, step, error);
    }

    [Event(EvtNativeEngineNote,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar engine reported progress")]
    public void NativeEngineNote(string name, string fields)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineNote, name, fields);
    }

    [Event(EvtNativeEngineDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "name={0} | fields={1}")]
    public void NativeEngineDetail(string name, string fields)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtNativeEngineDetail, name, fields);
    }

    [Event(EvtNativeEngineWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar engine worked around a problem\nname={0} | text={1} | fields={2}")]
    public void NativeEngineWarning(string name, string text, string fields)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineWarning, name, text, fields);
    }

    [Event(EvtNativeEngineError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar engine failed\nname={0} | text={1} | fields={2}")]
    public void NativeEngineError(string name, string text, string fields)
    {
        if (IsEnabled()) WriteEvent(EvtNativeEngineError, name, text, fields);
    }

    // ── Customization (what the settings ask of the engine) ───────────────────
    //
    // The stay of the engine in a process is bracketed by Injected and
    // Disconnected above. These say what the person asked for while it stays.
    // Parts are spelled background, compact_tray, compact_clock and start_menu.

    // parts: the parts now asked for, comma separated, or none. One line per
    // change of the set, so a log read later shows what was on and since when.
    [Event(EvtTaskbarPartsChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The taskbar customization changed")]
    public void TaskbarPartsChanged(string parts)
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarPartsChanged, parts);
    }

    // A part that is on but asks nothing of the engine. reason: taskbar_horizontal
    // (the compact tray is written for a vertical bar).
    [Event(EvtTaskbarPartSkipped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A taskbar customization is on but was left out")]
    public void TaskbarPartSkipped(string part, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarPartSkipped, part, reason);
    }

    // One Warning per incident: a part the engine cannot hold opens it, the
    // first status without the problem closes it (TaskbarPartProblemEnded).
    // problem: not_applied, contested or error.
    [Event(EvtTaskbarPartNotInEffect,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A taskbar customization is not in effect, so the taskbar may not look as asked\npart={0} | problem={1}")]
    public void TaskbarPartNotInEffect(string part, string problem)
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarPartNotInEffect, part, problem);
    }

    [Event(EvtTaskbarPartProblemEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A taskbar customization problem ended")]
    public void TaskbarPartProblemEnded(string part)
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarPartProblemEnded, part);
    }

    // target: explorer or start_menu. The DLL left with the pipe, not with its
    // own word, so explorer may still hold it until it exits.
    [Event(EvtTaskbarEngineUnloadUnconfirmed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar engine did not confirm that it unloaded\ntarget={0}")]
    public void TaskbarEngineUnloadUnconfirmed(string target)
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarEngineUnloadUnconfirmed, target);
    }

    // A pass over the settings threw. trigger: startup, setting or
    // engine_connected.
    [Event(EvtTaskbarCustomizerFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar customization could not be applied\ntrigger={0} | ex_type={1}")]
    public void TaskbarCustomizerFailed(string trigger, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarCustomizerFailed, trigger, ex_type);
    }
}