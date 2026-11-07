using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Deckle.App.SelfCheck;

// Shows a window the way the self-check needs it: rendered, never seen and never
// activated. The run happens while the maintainer works, and a window that
// appears on a monitor or takes the focus costs them their typing.
//
// Three things together make that so, each answering one way a window reaches the
// person:
//   - placed to the left of every monitor, before it is shown, so it never
//     occupies a pixel of the visible desktop (a window created and shown
//     first, then moved, would flash for a frame);
//   - WS_EX_NOACTIVATE, so no click, no show and no Win32 call of the run gives
//     it the focus, and WS_EX_TOOLWINDOW, so it has no taskbar button to flash;
//   - shown with SW_SHOWNOACTIVATE, never Window.Activate.
// Whether XAML still renders for a window nobody can see is not assumed: the
// self-check reads real pixels from it, and fails if there are none.
internal static class OffscreenWindow
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOWNOACTIVATE = 4;

    // Clear of the virtual desktop by this much on top of the window's own width,
    // so a window that is later made wider still stays off every monitor.
    private const int Margin = 4000;

    public static void ShowInactive(Window window)
    {
        nint hwnd = WindowNative.GetWindowHandle(window);

        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        // Where the window may stand: left of the whole virtual desktop, one
        // window width and a margin beyond its left edge.
        GetWindowRect(hwnd, out RECT rect);
        int width = Math.Max(rect.right - rect.left, 1);
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN) - width - Margin;
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        SetWindowPos(hwnd, 0, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(nint hwnd, int index, long value);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);
}
