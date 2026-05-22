using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell;

// Shell module provider. Couvre les capacités du shell système :
// message-only Win32 host (tray callback + global hotkeys), autostart
// HKCU\Run, gestion des hotkeys (registration + WM_INPUTLANGCHANGE),
// et l'utilitaire DispatcherQueueExtensions (warning quand une enqueue
// UI échoue).
//
// La doctrine "l'observation s'attache au module qui contient
// l'opération" fait converger plusieurs sources legacy
// (`LogSource.Hotkey`, `LogSource.MsgHost`, `LogSource.Settings` pour
// la branche autostart, plus le paramètre `source` libre de
// DispatcherQueueExtensions) vers un seul provider `Deckle.Shell` →
// tag SHELL dans la LogWindow. Léger renommage côté UX, attendu pour
// la migration ; les keywords distinguent les sous-domaines internes.
[EventSource(Name = "Deckle.Shell")]
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
    public const int EvtDispatcherEnqueueRejected   = 15;

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

    // ── DispatcherQueueExtensions ───────────────────────────────────────
    //
    // L'extension reçoit en paramètre une source label libre que le
    // caller fournit (ex. "HUD", "LOGWIN"). Pour ne pas porter ce string
    // libre dans le manifest ETW comme un champ structuré supplémentaire,
    // on le préfixe dans le message text — le payload ETW garde uniquement
    // le `what` (description de l'event perdu).
    [Event(EvtDispatcherEnqueueRejected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "[{0}] DispatcherQueue.TryEnqueue rejected ({1}) — UI event dropped")]
    public void DispatcherEnqueueRejected(string caller_source, string what)
    {
        if (IsEnabled()) WriteEvent(EvtDispatcherEnqueueRejected, caller_source, what);
    }
}
