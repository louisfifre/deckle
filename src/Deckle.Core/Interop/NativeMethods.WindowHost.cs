using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // ── Message-only window ──────────────────────────────────────────────────
    //
    // A message-only window has HWND_MESSAGE as its parent. It is invisible,
    // has no z-order, cannot be enumerated, receives no broadcast messages,
    // and simply dispatches messages sent to it.

    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x, int y,
        int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ── Hidden top-level window ──────────────────────────────────────────────
    //
    // A top-level window that is never shown dispatches messages like a
    // message-only one and, unlike it, receives the broadcasts the shell
    // sends to every top-level window (TaskbarCreated among them).
    // WS_EX_TOOLWINDOW keeps it out of Alt+Tab and the taskbar if it is ever
    // shown by mistake.

    public const uint WS_POPUP = 0x80000000;

    // A message registered by name gets one id per session, the same for
    // every process that asks for that name: this is how Explorer's
    // broadcasts reach windows it has never met.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    public static extern uint RegisterWindowMessage(string lpString);

    // ── Window timer ─────────────────────────────────────────────────────────
    //
    // WM_TIMER lands in the window's own pump: a retry scheduled with
    // SetTimer runs on the thread that owns the window, without blocking it.
    // Calling SetTimer again with the same id restarts that timer.

    public const uint WM_TIMER = 0x0113;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

}
