using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell;

// Shell module provider. Covers system shell capabilities: message-only Win32
// host (tray callback + global hotkeys), HKCU\Run autostart, hotkey management
// (registration + WM_INPUTLANGCHANGE).
//
// The "observation attaches to the module that contains the operation" doctrine
// converges several legacy sources (`LogSource.Hotkey`, `LogSource.MsgHost`,
// `LogSource.Settings` for the autostart branch) into a single `Deckle.Shell`
// provider → SHELL tag in LogWindow. Slight UX-side renaming, expected for the
// migration; keywords distinguish internal subdomains.
//
// The historical `DispatcherEnqueueRejected` event (formerly id 15 here) was
// migrated to `DeckleThreadingSource` during the cross-cutting instrumentation
// wave. It did not describe a shell operation; it described a dispatcher
// rejection crossing any module that marshals to the UI thread. Id 15 remains
// an intentional gap here to preserve stability for the remaining Shell event
// ids (listeners filtering by id have nothing to update).
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
    // Id 15 (DispatcherEnqueueRejected) migrated to DeckleThreadingSource:
    // intentional gap to avoid renumbering the remaining ids.

    // ── Message-only host ───────────────────────────────────────────────

    [Event(EvtMessageOnlyHostCreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "message-only window created hwnd={0}")]
    public void MessageOnlyHostCreated(long hwnd)
    {
        if (IsEnabled()) WriteEvent(EvtMessageOnlyHostCreated, hwnd);
    }

    // ── Autostart (HKCU\Run) ────────────────────────────────────────────

    [Event(EvtAutostartProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart probe failed | error={0}: {1}")]
    public void AutostartProbeFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartProbeFailed, ex_type, message);
    }

    [Event(EvtAutostartEnableSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart enable skipped | reason=Environment.ProcessPath empty")]
    public void AutostartEnableSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableSkipped);
    }

    [Event(EvtAutostartEnableFailedAcl,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autostart enable failed | reason=cannot open HKCU\\{0}")]
    public void AutostartEnableFailedAcl(string run_key_path)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailedAcl, run_key_path);
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
           Message = "autostart enable failed | error={0}: {1}")]
    public void AutostartEnableFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartEnableFailed, ex_type, message);
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
           Message = "autostart disable failed | error={0}: {1}")]
    public void AutostartDisableFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtAutostartDisableFailed, ex_type, message);
    }

    // ── Hotkeys ─────────────────────────────────────────────────────────

    [Event(EvtHotkeyVkResolveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "MapVirtualKeyExW returned 0 for scancode 0x29 (HKL {0:X}) — skipping register")]
    public void HotkeyVkResolveFailed(long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyVkResolveFailed, hkl);
    }

    [Event(EvtHotkeyRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "register scancode 0x29 → VK 0x{0:X2} under HKL {1:X}")]
    public void HotkeyRegistered(uint vk, long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyRegistered, vk, hkl);
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
           Message = "re-register failed: {0}")]
    public void HotkeyReregisterFailed(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailed, message);
    }

    // DispatcherEnqueueRejected now lives on DeckleThreadingSource. Callers go
    // through DispatcherQueueExtensions.TryEnqueueOrLog (which redirects
    // internally) or directly through the Threading provider EventSource path.
}
