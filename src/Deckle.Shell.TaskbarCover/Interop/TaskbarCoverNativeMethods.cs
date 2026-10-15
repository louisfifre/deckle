using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// Module-local Win32 surface for the cover host, beyond what Deckle.Core's
// NativeMethods declares: APIs consumed by a single module stay in that module.
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

    // ── Taskbar windows ──────────────────────────────────────────────────────
    //
    // Explorer owns one top-level window per taskbar: Shell_TrayWnd on the
    // primary monitor and a Shell_SecondaryTrayWnd on every other monitor that
    // shows one. SHAppBarMessage(ABM_GETTASKBARPOS), the former source, only
    // ever described the primary, which is why the cover used to stop at one
    // monitor. Walking the top-level windows by class with FindWindowEx(NULL,
    // after, class, NULL) reaches them all without sending Explorer anything,
    // so a hung shell cannot block the cover thread here.

    public const string PrimaryTaskbarClass   = "Shell_TrayWnd";
    public const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    // ── Monitor of a taskbar ─────────────────────────────────────────────────
    //
    // The monitor gives the band its edge (the taskbar rect against the
    // monitor rect), its DPI and the handle compared with the foreground
    // window's monitor for fullscreen suppression. DEFAULTTONEAREST: a taskbar
    // window always yields a monitor, even one slid off-screen by auto-hide.

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // The approach span is set in device-independent pixels while the band
    // rect and the cursor are physical pixels, so each band scales it by its
    // monitor's effective DPI. Returns an HRESULT, S_OK (0) on success.

    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ── Cursor WinEvent hook ─────────────────────────────────────────────────
    //
    // EVENT_OBJECT_LOCATIONCHANGE filtered on OBJID_CURSOR is the global
    // cursor-movement signal that does not contend with Raw Input — a
    // RIDEV_INPUTSINK mouse registration is per-process-per-usage and
    // CursorMovementSignal owns the only one (Deckle.Shell). Delivery is
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

    // ── Foreground hand-off ──────────────────────────────────────────────────
    //
    // The ForegroundWatcher raises Changed on the UI thread; the host posts
    // this private message to its anchor window so the cover thread, which
    // owns every state field, reads the snapshot itself. WM_APP range: private
    // to the window class, never sent by the system.

    public const uint WM_APP_FOREGROUND = 0x8000 + 1;

    // Same hand-off for the approach level: the App writes it through
    // TaskbarCoverHost.Approach from its own thread, the cover thread reads
    // it when this message lands and rebuilds the zones.
    public const uint WM_APP_APPROACH = 0x8000 + 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ── System messages the anchor window reacts to ──────────────────────────

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
}
