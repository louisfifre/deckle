using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// Module-local Win32 surface for the cover host — everything the band
// window, its message pump, the taskbar query, the cursor WinEvent hook
// and the suspend/suppression probes need beyond what Deckle.Core's
// NativeMethods already declares. Same precedent as TrayMenuNativeMethods:
// APIs consumed by a single module stay in that module.
internal static class TaskbarCoverNativeMethods
{
    // ── Message pump (dedicated thread) ──────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public POINT  pt;
    }

    public const uint WM_QUIT = 0x0012;

    // Unicode pinned explicitly: without CharSet these resolve to the *A
    // exports (user32 exports no bare names), an ANSI pump in front of a
    // window registered through RegisterClassExW. TranslateMessage has a
    // single export, no variant to pin.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // ── Window styles / class resources ──────────────────────────────────────

    public const uint WS_POPUP = 0x80000000;

    // Class background brush for the band — released with DeleteObject after
    // UnregisterClass (the system does not free class brushes on its own).
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    // ── Timers ────────────────────────────────────────────────────────────────

    public const uint WM_TIMER = 0x0113;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    // ── Taskbar position (SHAppBarMessage) ───────────────────────────────────
    //
    // ABM_GETTASKBARPOS fills uEdge (ABE_* — the TaskbarEdge ordinals) and rc
    // (taskbar rect in physical pixels). Returns zero on failure (no taskbar,
    // shell not ready).

    public const uint ABM_GETTASKBARPOS = 0x00000005;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint   cbSize;
        public IntPtr hWnd;
        public uint   uCallbackMessage;
        public uint   uEdge;
        public NativeMethods.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ── Cursor WinEvent hook ─────────────────────────────────────────────────
    //
    // EVENT_OBJECT_LOCATIONCHANGE filtered on OBJID_CURSOR is the global
    // cursor-movement signal that does not contend with Raw Input — a
    // RIDEV_INPUTSINK mouse registration is per-process-per-usage and
    // CursorMovementSignal owns the only one (see the module CLAUDE.md). Delivery is
    // asynchronous (no latency injected into the input chain, unlike a
    // WH_MOUSE_LL hook) on the thread that called SetWinEventHook, which
    // must pump messages; UnhookWinEvent must run on that same thread.
    // WINEVENT_OUTOFCONTEXT alone — no SKIPOWNPROCESS, the process a cursor
    // move is attributed to is undocumented and moves over our own windows
    // must not be lost. The delegate is rooted in a field for the GC.

    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const int  OBJID_CURSOR                = -9;
    public const uint WINEVENT_OUTOFCONTEXT       = 0x0000;

    public delegate void WinEventProc(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc pfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ── System messages the band window reacts to ────────────────────────────

    public const uint WM_DESTROY       = 0x0002;
    public const uint WM_SETTINGCHANGE = 0x001A; // taskbar moved/resized, DPI, work area
    public const uint WM_DISPLAYCHANGE = 0x007E; // resolution / monitor topology change

    // Broadcast by Explorer to all top-level windows each time the taskbar
    // is (re)created — registered by name, the ID is dynamic per session.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    // ── Sleep / session lock ─────────────────────────────────────────────────

    public const uint WM_POWERBROADCAST       = 0x0218;
    public const uint PBT_APMSUSPEND          = 0x0004;
    public const uint PBT_APMRESUMEAUTOMATIC  = 0x0012;

    public const uint WM_WTSSESSION_CHANGE  = 0x02B1;
    public const uint WTS_SESSION_LOCK      = 0x0007;
    public const uint WTS_SESSION_UNLOCK    = 0x0008;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;

    [DllImport("wtsapi32.dll")]
    public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll")]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    // ── Fullscreen suppression probes ────────────────────────────────────────
    //
    // Stage 1: SHQueryUserNotificationState — QUNS_BUSY (2),
    // QUNS_RUNNING_D3D_FULL_SCREEN (3) and QUNS_PRESENTATION_MODE (4) suppress
    // without touching geometry. Stage 2: the foreground window's DWM extended
    // frame bounds (visual rect without the invisible shadow margins) covering
    // its whole monitor.

    public const int QUNS_BUSY                    = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE       = 4;

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int pquns);

    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hWnd, uint dwAttribute, out NativeMethods.RECT pvAttribute, uint cbAttribute);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public NativeMethods.RECT rcMonitor;
        public NativeMethods.RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
