using Deckle.Core;

namespace Deckle.Shell.Foreground;

// The geometric half of the fullscreen test, kept free of Win32 so it can be
// tested: a window reads as fullscreen when its visual bounds reach or pass
// every edge of its monitor.
public static class FullscreenGeometry
{
    public static bool CoversMonitor(NativeMethods.RECT bounds, NativeMethods.RECT monitor) =>
        bounds.left <= monitor.left && bounds.top <= monitor.top
        && bounds.right >= monitor.right && bounds.bottom >= monitor.bottom;
}
