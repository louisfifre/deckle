using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.Taskbar;

// Taskbar module provider. Two pieces write here: the tray icon promoter (and
// its registry watcher) and the Recommended section policy. Counts and codes
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
    public const int EvtRecommendedSectionHidden     = 11;
    public const int EvtRecommendedSectionShown      = 12;
    public const int EvtRecommendedPolicyFailed      = 13;
    public const int EvtRecommendedPolicyDetail      = 14;
    public const int EvtSettingsLoadWarning          = 15;
    public const int EvtSettingsLoadError            = 16;
    public const int EvtExplorerRestartReapplyFailed = 17;

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

    // ── Recommended section policy ────────────────────────────────────────────
    //
    // A change is a gesture of the person (the card's switch), so each one
    // that wrote gets its milestone. A declined prompt or a failed write is one
    // Warning with its cause.

    [Event(EvtRecommendedSectionHidden,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Start's Recommended section is set to hide")]
    public void RecommendedSectionHidden()
    {
        if (IsEnabled()) WriteEvent(EvtRecommendedSectionHidden);
    }

    [Event(EvtRecommendedSectionShown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Gave Start's Recommended section back")]
    public void RecommendedSectionShown()
    {
        if (IsEnabled()) WriteEvent(EvtRecommendedSectionShown);
    }

    // action: hide or show. route: direct (Deckle runs elevated), elevated
    // (reg.exe behind a UAC prompt), machine or in_place (nothing to write)
    // or none. error: none, uac_declined, exit_N, other_hive or an exception
    // type.
    [Event(EvtRecommendedPolicyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Start's Recommended section was not changed\naction={0} | route={1} | outcome={2} | error={3}")]
    public void RecommendedPolicyFailed(string action, string route, string outcome, string error)
    {
        if (IsEnabled()) WriteEvent(EvtRecommendedPolicyFailed, action, route, outcome, error);
    }

    // Every attempt, whatever it ended in, with the same fields as the failure.
    [Event(EvtRecommendedPolicyDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "action={0} | route={1} | outcome={2} | error={3}")]
    public void RecommendedPolicyDetail(string action, string route, string outcome, string error)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtRecommendedPolicyDetail, action, route, outcome, error);
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
}
