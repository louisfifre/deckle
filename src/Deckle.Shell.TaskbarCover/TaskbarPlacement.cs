using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// Where one taskbar sits: its window, its rect in physical pixels, the edge
// it hugs, its monitor and that monitor's DPI over 96. Pure data, one per
// taskbar on the desktop; the host lays one band over each.
public readonly record struct TaskbarPlacement(
    IntPtr Taskbar,
    NativeMethods.RECT Rect,
    TaskbarEdge Edge,
    IntPtr Monitor,
    double DpiScale)
{
    // Desktop order, top to bottom then left to right. The locator sorts the
    // secondary taskbars by it so a band keeps the same taskbar from one
    // layout to the next whatever the z-order FindWindowEx walks them in;
    // two taskbars never share a top-left, each sits on its own monitor.
    public static int CompareDesktopOrder(TaskbarPlacement a, TaskbarPlacement b)
    {
        int byTop = a.Rect.top.CompareTo(b.Rect.top);
        return byTop != 0 ? byTop : a.Rect.left.CompareTo(b.Rect.left);
    }
}
