using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell;

// Shell module provider. Covers system shell capabilities: message-only Win32
// host (tray callback + global hotkeys), HKCU\Run autostart, elevated startup
// (Task Scheduler vehicle), hotkey management (registration +
// WM_INPUTLANGCHANGE).
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
    public const int EvtElevatedStartupEnabled      = 16;
    public const int EvtElevatedStartupEnableFailed  = 17;
    public const int EvtElevatedStartupDisabled     = 18;
    public const int EvtElevatedStartupDisableFailed = 19;
    public const int EvtElevatedStartupProbeFailed   = 20;
    // Verbose mirrors appended for the Verbose/Info separation: each milestone
    // above whose message carried an error / path / handle now emits a short
    // Capital sentence, and the technical detail moves to one of these fresh
    // ids. IDs are public in the ETW manifest; never reuse an id.
    public const int EvtAutostartProbeFailedDetail        = 21;
    public const int EvtAutostartEnableFailedAclDetail    = 22;
    public const int EvtAutostartEnableFailedDetail       = 23;
    public const int EvtAutostartDisableFailedDetail      = 24;
    public const int EvtHotkeyVkResolveFailedDetail       = 25;
    public const int EvtElevatedStartupEnableFailedDetail  = 26;
    public const int EvtElevatedStartupDisableFailedDetail = 27;
    public const int EvtElevatedStartupProbeFailedDetail   = 28;
    public const int EvtHotkeyReregisterFailedDetail      = 29;

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

    // Constant reason (Environment.ProcessPath empty) documented at the call
    // site; the milestone carries no detail, so no Verbose mirror is needed.
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
           Message = "hotkey vk resolve failed | scancode=0x29 | hkl=0x{0:X}")]
    public void HotkeyVkResolveFailedDetail(long hkl)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyVkResolveFailedDetail, hkl);
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
           Message = "Could not re-register the hotkeys after a keyboard layout change")]
    public void HotkeyReregisterFailed()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailed);
    }

    [Event(EvtHotkeyReregisterFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "hotkey re-register failed | message={0}")]
    public void HotkeyReregisterFailedDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyReregisterFailedDetail, message);
    }

    // DispatcherEnqueueRejected now lives on DeckleThreadingSource. Callers go
    // through DispatcherQueueExtensions.TryEnqueueOrLog (which redirects
    // internally) or directly through the Threading provider EventSource path.
}
