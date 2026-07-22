using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Input;

// Low-level mouse hook plumbing for OS mouse-wheel messages. Raw Input sees
// physical mouse TLC reports, but Precision Touchpad two-finger scroll can
// arrive only as WM_MOUSEWHEEL / WM_MOUSEHWHEEL in the normal mouse message
// stream. WH_MOUSE_LL gives the input host one global observation point for
// those messages without injecting into other processes.
public static class LowLevelMouseHookInterop
{
    public const int WH_MOUSE_LL = 14;

    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MOUSEWHEEL  = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_MOUSEHWHEEL = 0x020E;

    public const uint LLMHF_INJECTED = 0x00000001;
    public const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

    public static bool IsButtonDown(int message) => message is
        WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static short GetWheelDelta(uint mouseData) =>
        unchecked((short)((mouseData >> 16) & 0xFFFF));

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hmod,
        uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);
}
