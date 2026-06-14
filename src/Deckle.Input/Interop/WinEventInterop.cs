using System.Runtime.InteropServices;

namespace Deckle.Input;

// WinEvent hook plumbing for focus-change observation. SetWinEventHook
// with WINEVENT_OUTOFCONTEXT delivers callbacks via the host thread's
// message pump (no DLL injection), which is why the hooks must be
// installed from the thread that owns the pump — that thread is where
// WinEventProc fires. The delegate handed to SetWinEventHook must stay
// rooted for the hook's lifetime, same GC rule as a WndProc.
public static class WinEventInterop
{
    // Out-of-context: callbacks marshalled to the installing thread's
    // message queue. Skip-own-process: never report focus moves caused by
    // Deckle's own windows.
    public const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    // Foreground window changed (Alt+Tab, click into another app).
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    // Keyboard focus moved within the foreground app (control to control).
    public const uint EVENT_OBJECT_FOCUS      = 0x8005;

    public delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
