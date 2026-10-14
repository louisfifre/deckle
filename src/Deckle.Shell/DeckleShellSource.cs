using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell;

[EventSource(Name = "Deckle-Shell")]
public sealed class DeckleShellSource : DeckleEventSource
{
    public static readonly DeckleShellSource Log = new();

    private DeckleShellSource() { }

    public const int EvtMessageOnlyHostCreated      = 1;
    public const int EvtAutostartProbeFailed        = 2;
    public const int EvtAutostartEnableSkipped      = 3;
    public const int EvtAutostartEnableFailedAcl    = 4;
    public const int EvtAutostartEnabled            = 5;
    public const int EvtAutostartEnabledDetail      = 6;
    public const int EvtAutostartEnableFailed       = 7;
    public const int EvtAutostartDisableSkipped     = 8;
    public const int EvtAutostartDisabled           = 9;
    public const int EvtAutostartDisableFailed      = 10;
    public const int EvtHotkeyVkResolveFailed       = 11;
    public const int EvtHotkeyRegistered            = 12;
    public const int EvtHotkeyLayoutChange          = 13;
    public const int EvtHotkeyReregisterFailed      = 14;
    // Id 15 is retired; never reuse it.
    public const int EvtElevatedStartupEnabled      = 16;
    public const int EvtElevatedStartupEnableFailed  = 17;
    public const int EvtElevatedStartupDisabled     = 18;
    public const int EvtElevatedStartupDisableFailed = 19;
    public const int EvtElevatedStartupProbeFailed   = 20;
    // IDs are public in the ETW manifest; never reuse an id.
    public const int EvtAutostartProbeFailedDetail        = 21;
    public const int EvtAutostartEnableFailedAclDetail    = 22;
    public const int EvtAutostartEnableFailedDetail       = 23;
    public const int EvtAutostartDisableFailedDetail      = 24;
    public const int EvtHotkeyVkResolveFailedDetail       = 25;
    public const int EvtElevatedStartupEnableFailedDetail  = 26;
    public const int EvtElevatedStartupDisableFailedDetail = 27;
    public const int EvtElevatedStartupProbeFailedDetail   = 28;
    public const int EvtHotkeyReregisterFailedDetail      = 29;
    public const int EvtCursorSignalArmed                  = 30;
    public const int EvtCursorSignalRegistrationFailed     = 31;
    public const int EvtCursorSignalRegistrationFailedDetail = 32;
    public const int EvtTrayIconHostCreated                = 33;
    public const int EvtTaskbarRecreated                   = 34;
    public const int EvtTrayIconReaddAttempt               = 35;
    public const int EvtTrayIconRestored                   = 36;
    public const int EvtTrayIconRestoreFailed              = 37;

    // ── Tray icon ───────────────────────────────────────────────────────

    // Both long: a (long, uint) pair lands on the WriteEvent(long, long)
    // overload and the payload no longer matches the declared types.
    [Event(EvtTrayIconHostCreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tray icon owner window created | hwnd={0} | taskbar_created_msg={1}")]
    public void TrayIconHostCreated(long hwnd, long taskbar_created_msg)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconHostCreated, hwnd, taskbar_created_msg);
    }

    [Event(EvtTaskbarRecreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "TaskbarCreated received — adding the tray icon again")]
    public void TaskbarRecreated()
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarRecreated);
    }

    // One row per attempt; the incident is the Warning below, once.
    [Event(EvtTrayIconReaddAttempt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tray icon re-add | attempt={0}/{1} | outcome={2} | error={3}")]
    public void TrayIconReaddAttempt(int attempt, int max_attempts, string outcome, int error)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconReaddAttempt, attempt, max_attempts, outcome, error);
    }

    [Event(EvtTrayIconRestored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Taskbar recreated, tray icon added again")]
    public void TrayIconRestored()
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconRestored);
    }

    [Event(EvtTrayIconRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Taskbar recreated, but the tray icon could not be added again — restart Deckle to get it back")]
    public void TrayIconRestoreFailed()
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconRestoreFailed);
    }

    // ── Message-only host ───────────────────────────────────────────────

    [Event(EvtMessageOnlyHostCreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "message-only window created hwnd={0}")]
    public void MessageOnlyHostCreated(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtMessageOnlyHostCreated, hwnd);
    }

    // ── Cursor movement signal ──────────────────────────────────────────

    [Event(EvtCursorSignalArmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cursor movement signal armed | hwnd={0}")]
    public void CursorSignalArmed(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtCursorSignalArmed, hwnd);
    }

    [Event(EvtCursorSignalRegistrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not arm the cursor proximity signal — proximity fade is off")]
    public void CursorSignalRegistrationFailed()
    {
        if (IsEnabled()) WriteEvent(EvtCursorSignalRegistrationFailed);
    }

    [Event(EvtCursorSignalRegistrationFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cursor signal registration failed | error={0}")]
    public void CursorSignalRegistrationFailedDetail(int error)
    {
        if (IsEnabled()) WriteEvent(EvtCursorSignalRegistrationFailedDetail, error);
    }

    // ── Autostart (HKCU\Run) ────────────────────────────────────────────

    [Event(EvtAutostartProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not read the autostart setting")]
    public void AutostartProbeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartProbeFailed);
    }

    [Event(EvtAutostartProbeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart probe failed | error={0} | message={1}")]
    public void AutostartProbeFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartProbeFailedDetail, ex_type, message);
    }

    // No Verbose mirror: the only cause is an empty Environment.ProcessPath.
    [Event(EvtAutostartEnableSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autostart was not enabled because the program path is unknown")]
    public void AutostartEnableSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableSkipped);
    }

    [Event(EvtAutostartEnableFailedAcl,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not enable autostart because the registry was not writable")]
    public void AutostartEnableFailedAcl()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailedAcl);
    }

    [Event(EvtAutostartEnableFailedAclDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart enable failed | reason=cannot_open | run_key_path={0}")]
    public void AutostartEnableFailedAclDetail(string run_key_path)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailedAclDetail, run_key_path);
    }

    [Event(EvtAutostartEnabled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autostart enabled")]
    public void AutostartEnabled()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnabled);
    }

    [Event(EvtAutostartEnabledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart enabled | command={0}")]
    public void AutostartEnabledDetail(string command)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnabledDetail, command);
    }

    [Event(EvtAutostartEnableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not enable autostart")]
    public void AutostartEnableFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailed);
    }

    [Event(EvtAutostartEnableFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart enable failed | error={0} | message={1}")]
    public void AutostartEnableFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailedDetail, ex_type, message);
    }

    [Event(EvtAutostartDisableSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart disable skipped | reason=entry points to different install")]
    public void AutostartDisableSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartDisableSkipped);
    }

    [Event(EvtAutostartDisabled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autostart disabled")]
    public void AutostartDisabled()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartDisabled);
    }

    [Event(EvtAutostartDisableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not disable autostart")]
    public void AutostartDisableFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartDisableFailed);
    }

    [Event(EvtAutostartDisableFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart disable failed | error={0} | message={1}")]
    public void AutostartDisableFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartDisableFailedDetail, ex_type, message);
    }

    // ── Elevated startup (Task Scheduler) ───────────────────────────────

    [Event(EvtElevatedStartupEnabled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Elevated startup enabled")]
    public void ElevatedStartupEnabled()
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupEnabled);
    }

    [Event(EvtElevatedStartupEnableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not enable elevated startup")]
    public void ElevatedStartupEnableFailed()
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupEnableFailed);
    }

    [Event(EvtElevatedStartupEnableFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "elevated startup enable failed | error={0} | message={1}")]
    public void ElevatedStartupEnableFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupEnableFailedDetail, ex_type, message);
    }

    [Event(EvtElevatedStartupDisabled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Elevated startup disabled")]
    public void ElevatedStartupDisabled()
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupDisabled);
    }

    [Event(EvtElevatedStartupDisableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not disable elevated startup")]
    public void ElevatedStartupDisableFailed()
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupDisableFailed);
    }

    [Event(EvtElevatedStartupDisableFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "elevated startup disable failed | error={0} | message={1}")]
    public void ElevatedStartupDisableFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupDisableFailedDetail, ex_type, message);
    }

    [Event(EvtElevatedStartupProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not read the elevated startup setting")]
    public void ElevatedStartupProbeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupProbeFailed);
    }

    [Event(EvtElevatedStartupProbeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "elevated startup probe failed | error={0} | message={1}")]
    public void ElevatedStartupProbeFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupProbeFailedDetail, ex_type, message);
    }

    // ── Hotkeys ─────────────────────────────────────────────────────────

    [Event(EvtHotkeyVkResolveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Could not resolve the hotkey for the current keyboard layout")]
    public void HotkeyVkResolveFailed()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyVkResolveFailed);
    }

    [Event(EvtHotkeyVkResolveFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 1,
           Message = "hotkey vk resolve failed | scancode=0x{0:X} | hkl=0x{1:X}")]
    public void HotkeyVkResolveFailedDetail(uint scancode, long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyVkResolveFailedDetail, scancode, hkl);
    }

    [Event(EvtHotkeyRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 1,
           Message = "hotkey registered | id={0} | scancode=0x{1:X} | vk=0x{2:X2} | hkl=0x{3:X}")]
    public void HotkeyRegistered(int id, uint scancode, uint vk, long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyRegistered, id, scancode, vk, hkl);
    }

    [Event(EvtHotkeyLayoutChange,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "WM_INPUTLANGCHANGE — re-registering hotkeys")]
    public void HotkeyLayoutChange()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyLayoutChange);
    }

    [Event(EvtHotkeyReregisterFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Some hotkeys could not be re-registered after a keyboard layout change")]
    public void HotkeyReregisterFailed()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailed);
    }

    [Event(EvtHotkeyReregisterFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 1,
           Message = "hotkey re-register failed | failures={0}")]
    public void HotkeyReregisterFailedDetail(string failures)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailedDetail, failures);
    }
}
