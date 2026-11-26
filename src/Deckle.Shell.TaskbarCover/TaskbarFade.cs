using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// The taskbar's own opacity. The cover no longer draws anything: it fades
// Explorer's taskbar window itself, through WS_EX_LAYERED and
// SetLayeredWindowAttributes(LWA_ALPHA), which Windows 11 honours on
// Shell_TrayWnd from another process (observed on build 26100: the icons fade
// over the wallpaper when the taskbar background is transparent). The window
// belongs to Explorer, so whatever Deckle sets stays set until Deckle restores
// it, Explorer re-creates the taskbar or the session ends.
public static class TaskbarFade
{
    public const byte Opaque = 255;

    // The faintest the taskbar gets. At 0 Windows stops hit-testing the window
    // and a click goes through to the desktop; at 1 the taskbar is invisible
    // and still takes the click (both observed on build 26100).
    public const byte Floor = 1;

    // The taskbar's alpha for a cover level: 0 leaves the taskbar whole, 255
    // fades it to the floor.
    public static byte AlphaFor(byte coverLevel) => (byte)Math.Max(Floor, Opaque - coverLevel);

    // Sets the taskbar's alpha, making it layered first when it is not.
    // Returns 0, or the Win32 error of the call that failed.
    public static int Apply(IntPtr taskbar, byte alpha)
    {
        if (!TryReadExStyle(taskbar, out long exStyle, out int error)) return error;

        if ((exStyle & NativeMethods.WS_EX_LAYERED) == 0)
        {
            Marshal.SetLastPInvokeError(0);
            if (NativeMethods.SetWindowLongPtr(taskbar, NativeMethods.GWL_EXSTYLE,
                    (IntPtr)(exStyle | NativeMethods.WS_EX_LAYERED)) == IntPtr.Zero)
            {
                error = Marshal.GetLastPInvokeError();
                if (error != 0) return error;
            }
        }

        return NativeMethods.SetLayeredWindowAttributes(taskbar, 0, alpha, NativeMethods.LWA_ALPHA)
            ? 0
            : Marshal.GetLastPInvokeError();
    }

    // Gives the taskbar back as Explorer made it: opaque, then no longer
    // layered. Explorer does not make it layered itself (its extended style
    // reads TOPMOST | TOOLWINDOW on build 26100), so a layered taskbar is one
    // Deckle faded. Safe on a window that is gone.
    public static void Restore(IntPtr taskbar)
    {
        if (taskbar == IntPtr.Zero) return;
        if (!TryReadExStyle(taskbar, out long exStyle, out _)) return;
        if ((exStyle & NativeMethods.WS_EX_LAYERED) == 0) return;

        NativeMethods.SetLayeredWindowAttributes(taskbar, 0, Opaque, NativeMethods.LWA_ALPHA);
        NativeMethods.SetWindowLongPtr(taskbar, NativeMethods.GWL_EXSTYLE,
            (IntPtr)(exStyle & ~(long)NativeMethods.WS_EX_LAYERED));
    }

    // GetWindowLongPtr returns 0 both for a failure and for a window without
    // extended styles; the last error tells them apart.
    private static bool TryReadExStyle(IntPtr hwnd, out long exStyle, out int error)
    {
        Marshal.SetLastPInvokeError(0);
        exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        error = exStyle == 0 ? Marshal.GetLastPInvokeError() : 0;
        return error == 0;
    }
}
