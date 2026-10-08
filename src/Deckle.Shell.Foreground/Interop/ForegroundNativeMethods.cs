using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell.Foreground;

// Module-local Win32 surface for the foreground watcher, beyond what
// Deckle.Core's NativeMethods already declares (GetForegroundWindow,
// GetWindowThreadProcessId, GetClassName, RECT). Same precedent as
// TaskbarCoverNativeMethods: APIs consumed by a single module stay in it.
internal static class ForegroundNativeMethods
{
    // ── WinEvent hooks ───────────────────────────────────────────────────────
    //
    // Out-of-context hooks deliver on the thread that called SetWinEventHook,
    // which must pump messages; UnhookWinEvent fails from any other thread.
    // idProcess != 0 with idThread == 0 narrows a hook to every thread of
    // that one process; idProcess == 0 means every process on the desktop.
    // WINEVENT_OUTOFCONTEXT alone: no SKIPOWNPROCESS, Deckle's own windows
    // can be foreground too.

    public const uint EVENT_SYSTEM_FOREGROUND     = 0x0003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const int  OBJID_WINDOW                = 0;
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

    // ── Desktop exclusion ────────────────────────────────────────────────────
    //
    // The desktop covers its whole monitor: clicking the wallpaper makes it
    // foreground, and without these guards it would read as a fullscreen app.

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    // ── Fullscreen probes ────────────────────────────────────────────────────
    //
    // Stage 1: SHQueryUserNotificationState — QUNS_BUSY (2),
    // QUNS_RUNNING_D3D_FULL_SCREEN (3) and QUNS_PRESENTATION_MODE (4).
    // Stage 2: the window's DWM extended frame bounds (visual rect without
    // the invisible shadow margins) against its monitor rect.

    public const int QUNS_BUSY                    = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE       = 4;

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int pquns);

    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hWnd, uint dwAttribute, out NativeMethods.RECT pvAttribute, uint cbAttribute);

    // ── Monitor ──────────────────────────────────────────────────────────────

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    // MONITORINFOEXW: MONITORINFO plus the monitor's GDI device name
    // (szDevice, CCHDEVICENAME = 32 WCHARs, e.g. "\\.\DISPLAY1"), the name
    // Ambient and the games registry key monitors by. cbSize must be
    // sizeof(MONITORINFOEXW) for GetMonitorInfoW to fill szDevice.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public uint cbSize;
        public NativeMethods.RECT rcMonitor;
        public NativeMethods.RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // ── Process image name ───────────────────────────────────────────────────
    //
    // PROCESS_QUERY_LIMITED_INFORMATION is enough for
    // QueryFullProcessImageNameW and is granted for most processes, elevated
    // ones included, where PROCESS_QUERY_INFORMATION would be refused.

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags, char[] lpExeName, ref uint lpdwSize);
}
