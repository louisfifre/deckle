using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell;

[EventSource(Name = "Deckle-Shell")]
public sealed class DeckleShellSource : DeckleEventSource
{
    public static readonly DeckleShellSource Log = new();

    private DeckleShellSource() { }

    public const int EvtMessageOnlyHostCreated      = 1;
    // 2 retired.
    public const int EvtAutostartEnableSkipped      = 3;
    public const int EvtAutostartEnableFailedAcl    = 4;
    public const int EvtAutostartEnabled            = 5;
    public const int EvtAutostartEnabledDetail      = 6;
    // 7 retired.
    public const int EvtAutostartDisableSkipped     = 8;
    public const int EvtAutostartDisabled           = 9;
    // 10 retired.
    // 11 retired.
    public const int EvtHotkeyRegistered            = 12;
    public const int EvtHotkeyLayoutChange          = 13;
    // 14 retired.
    // Id 15 is retired; never reuse it.
    public const int EvtElevatedStartupEnabled      = 16;
    // 17 retired.
    public const int EvtElevatedStartupDisabled     = 18;
    // 19 retired.
    // 20 retired.
    // IDs are public in the ETW manifest; never reuse an id.
    // 21 retired.
    public const int EvtAutostartEnableFailedAclDetail    = 22;
    // 23 retired.
    // 24 retired.
    // 25 retired.
    // 26 retired.
    // 27 retired.
    // 28 retired.
    public const int EvtHotkeyReregisterFailedDetail      = 29;
    public const int EvtCursorSignalArmed                  = 30;
    // 31 retired.
    // 32 retired.
    public const int EvtTrayIconHostCreated                = 33;
    public const int EvtTaskbarRecreated                   = 34;
    public const int EvtTrayIconReaddAttempt               = 35;
    public const int EvtTrayIconRestored                   = 36;
    // 37 retired.
    public const int EvtAutostartProbeFailed               = 38;
    public const int EvtAutostartEnableFailed              = 39;
    public const int EvtAutostartDisableFailed             = 40;
    public const int EvtElevatedStartupEnableFailed        = 41;
    public const int EvtElevatedStartupDisableFailed       = 42;
    public const int EvtElevatedStartupProbeFailed         = 43;
    public const int EvtElevatedStartupTaskUnreadable      = 44;
    public const int EvtElevatedStartupTempNotDeleted      = 45;
    public const int EvtCursorSignalRegistrationFailed     = 46;
    public const int EvtHotkeyVkResolveFailed              = 47;
    public const int EvtHotkeyReregisterFailed             = 48;
    public const int EvtTrayIconRestoreFailed              = 49;
    public const int EvtHotkeyUnregistered                 = 50;
    public const int EvtHotkeyReleaseFailed                = 51;
    public const int EvtHotkeySubclassFailed               = 52;
    public const int EvtMessageOnlyHostDestroyed           = 53;
    public const int EvtCursorSignalDisarmed               = 54;
    public const int EvtTrayIconHostDestroyed              = 55;
    public const int EvtWindowSubclassFailed               = 56;
    public const int EvtTrayIconFallback                   = 57;

    // ── Tray icon ───────────────────────────────────────────────────────

    // Both long: a (long, uint) pair lands on the WriteEvent(long, long)
    // overload and the payload no longer matches the declared types.
    [Event(EvtTrayIconHostCreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd={0} | taskbar_created_msg={1}")]
    public void TrayIconHostCreated(long hwnd, long taskbar_created_msg)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconHostCreated, hwnd, taskbar_created_msg);
    }

    [Event(EvtTrayIconHostDestroyed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd={0}")]
    public void TrayIconHostDestroyed(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconHostDestroyed, hwnd);
    }

    [Event(EvtTaskbarRecreated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Explorer restarted, so the tray icon is added again")]
    public void TaskbarRecreated()
    {
        if (IsEnabled()) WriteEvent(EvtTaskbarRecreated);
    }

    // One row per attempt; the incident is the Warning below, once.
    [Event(EvtTrayIconReaddAttempt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "attempt={0}/{1} | outcome={2} | error={3}")]
    public void TrayIconReaddAttempt(int attempt, int max_attempts, string outcome, int error)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconReaddAttempt, attempt, max_attempts, outcome, error);
    }

    [Event(EvtTrayIconRestored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Taskbar recreated, tray icon added again")]
    public void TrayIconRestored()
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconRestored);
    }

    // win32_error is the shell's refusal on the last attempt of the sequence.
    [Event(EvtTrayIconRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Taskbar recreated, but the tray icon could not be added again — restart Deckle to get it back\nattempts={0} | win32_error={1}")]
    public void TrayIconRestoreFailed(int attempts, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconRestoreFailed, attempts, win32_error);
    }

    // The tray shows the generic application icon instead of Deckle's own.
    // state: idle or recording (the icon asked for). reason: asset_missing (the
    // .ico is not next to the exe) or load_failed (the file is there but
    // Windows refused it; win32_error says why, 0 when the file is missing).
    [Event(EvtTrayIconFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray icon fell back to the generic application icon\nstate={0} | reason={1} | win32_error={2}")]
    public void TrayIconFallback(string state, string reason, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtTrayIconFallback, state, reason, win32_error);
    }

    // ── Message-only host ───────────────────────────────────────────────

    [Event(EvtMessageOnlyHostCreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd={0}")]
    public void MessageOnlyHostCreated(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtMessageOnlyHostCreated, hwnd);
    }

    [Event(EvtMessageOnlyHostDestroyed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd={0}")]
    public void MessageOnlyHostDestroyed(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtMessageOnlyHostDestroyed, hwnd);
    }

    // ── Resize coalescing ───────────────────────────────────────────────

    // window: the DeckleWindowingSource vocabulary. Register throws right
    // after; this line keeps the window and the code whatever the caller does
    // with the exception.
    [Event(EvtWindowSubclassFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "Could not watch window resizing\nwindow={0} | win32_error={1}")]
    public void WindowSubclassFailed(string window, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtWindowSubclassFailed, window, win32_error);
    }

    // ── Cursor movement signal ──────────────────────────────────────────

    [Event(EvtCursorSignalArmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd={0}")]
    public void CursorSignalArmed(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtCursorSignalArmed, hwnd);
    }

    [Event(EvtCursorSignalDisarmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd={0}")]
    public void CursorSignalDisarmed(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtCursorSignalDisarmed, hwnd);
    }

    // stage: "subclass" (SetWindowSubclass) or "raw_input"
    // (RegisterRawInputDevices), the call that refused.
    [Event(EvtCursorSignalRegistrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not arm the cursor proximity signal — proximity fade is off\nstage={0} | win32_error={1}")]
    public void CursorSignalRegistrationFailed(string stage, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtCursorSignalRegistrationFailed, stage, win32_error);
    }

    // ── Autostart (HKCU\Run) ────────────────────────────────────────────

    [Event(EvtAutostartProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The autostart setting could not be read, so it shows as off\nex_type={0} | ex_message={1}")]
    public void AutostartProbeFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartProbeFailed, ex_type, ex_message);
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
           Message = "run_key_path={0}")]
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
           Message = "command={0}")]
    public void AutostartEnabledDetail(string command)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnabledDetail, command);
    }

    [Event(EvtAutostartEnableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autostart could not be turned on, so Deckle will not start at sign-in\nex_type={0} | ex_message={1}")]
    public void AutostartEnableFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailed, ex_type, ex_message);
    }

    [Event(EvtAutostartDisableSkipped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autostart was left in place because it starts another Deckle install")]
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
           Message = "Autostart could not be turned off, so Deckle still starts at sign-in\nex_type={0} | ex_message={1}")]
    public void AutostartDisableFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartDisableFailed, ex_type, ex_message);
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

    // reason: program_path_unknown, schtasks_failed (exit_code set),
    // uac_declined, exception. The fields a reason does not use stay 0 or "".
    [Event(EvtElevatedStartupEnableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Elevated startup could not be turned on, so Deckle will not start as administrator at sign-in\nreason={0} | exit_code={1} | ex_type={2} | ex_message={3}")]
    public void ElevatedStartupEnableFailed(string reason, int exit_code, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupEnableFailed, reason, exit_code, ex_type, ex_message);
    }

    [Event(EvtElevatedStartupDisabled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Elevated startup disabled")]
    public void ElevatedStartupDisabled()
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupDisabled);
    }

    // Same reason vocabulary as ElevatedStartupEnableFailed, without
    // program_path_unknown.
    [Event(EvtElevatedStartupDisableFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Elevated startup could not be turned off, so Deckle may still start as administrator at sign-in\nreason={0} | exit_code={1} | ex_type={2} | ex_message={3}")]
    public void ElevatedStartupDisableFailed(string reason, int exit_code, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupDisableFailed, reason, exit_code, ex_type, ex_message);
    }

    [Event(EvtElevatedStartupProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The elevated startup setting could not be read, so it shows as off\nex_type={0} | ex_message={1}")]
    public void ElevatedStartupProbeFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupProbeFailed, ex_type, ex_message);
    }

    // The task exists but its XML did not parse, so the toggle reads off even
    // though a task named Deckle may still start the app at logon.
    [Event(EvtElevatedStartupTaskUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not read the elevated startup task, the setting shows off\nex_type={0} | ex_message={1}")]
    public void ElevatedStartupTaskUnreadable(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupTaskUnreadable, ex_type, ex_message);
    }

    [Event(EvtElevatedStartupTempNotDeleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0} | ex_type={1} | ex_message={2}")]
    public void ElevatedStartupTempNotDeleted(string path, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtElevatedStartupTempNotDeleted, path, ex_type, ex_message);
    }

    // ── Hotkeys ─────────────────────────────────────────────────────────

    // Without the WM_HOTKEY subclass no chord reaches the app, whatever the
    // registrations say. SetWindowSubclass documents no GetLastError, so
    // win32_error may read 0.
    [Event(EvtHotkeySubclassFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Could not install the hotkey window hook — no global hotkey will work\nwin32_error={0}")]
    public void HotkeySubclassFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeySubclassFailed, win32_error);
    }

    // Both long for the reason given on TrayIconHostCreated.
    [Event(EvtHotkeyVkResolveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A hotkey key does not exist on the current keyboard layout, so its shortcut does nothing\nscancode=0x{0:X} | hkl=0x{1:X}")]
    public void HotkeyVkResolveFailed(long scancode, long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyVkResolveFailed, scancode, hkl);
    }

    [Event(EvtHotkeyRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 1,
           Message = "id={0} | scancode=0x{1:X} | vk=0x{2:X2} | hkl=0x{3:X}")]
    public void HotkeyRegistered(int id, uint scancode, uint vk, long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyRegistered, id, scancode, vk, hkl);
    }

    // The counterpart of HotkeyRegistered, on a rebind, a layout change and
    // Dispose, so the set held at any moment reads off the log.
    [Event(EvtHotkeyUnregistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "id={0}")]
    public void HotkeyUnregistered(int id)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyUnregistered, id);
    }

    // The chord stays held; the next registration pass reports it as a
    // failure with this same error.
    [Event(EvtHotkeyReleaseFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "id={0} | win32_error={1}")]
    public void HotkeyReleaseFailed(int id, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReleaseFailed, id, win32_error);
    }

    [Event(EvtHotkeyLayoutChange,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The keyboard layout changed, so hotkeys are registered again")]
    public void HotkeyLayoutChange()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyLayoutChange);
    }

    // win32_error is the first failure's; the Verbose detail lists them all.
    [Event(EvtHotkeyReregisterFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Some hotkeys could not be registered again after a keyboard layout change, so their shortcuts do nothing\nfailed={0} | win32_error={1}")]
    public void HotkeyReregisterFailed(int failed, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailed, failed, win32_error);
    }

    [Event(EvtHotkeyReregisterFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 1,
           Message = "failures={0}")]
    public void HotkeyReregisterFailedDetail(string failures)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailedDetail, failures);
    }
}
