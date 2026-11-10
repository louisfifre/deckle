using Deckle.Core;

namespace Deckle.Shell.Foreground;

// The geometric half of the fullscreen test, kept free of Win32 so it can be
// tested: a window reads as fullscreen when it is drawn and its visual
// bounds reach or pass every edge of its monitor.
public static class FullscreenGeometry
{
    // Drawn means visible and not cloaked. The Alt+Tab switcher is a
    // transparent Explorer window the size of the monitor, and Windows can
    // name it in a foreground event after it has already been hidden: by
    // size alone it read as fullscreen and kept the taskbar cover down.
    public static bool ReadsAsFullscreen(bool drawn, NativeMethods.RECT bounds, NativeMethods.RECT monitor) =>
        drawn && CoversMonitor(bounds, monitor);

    public static bool CoversMonitor(NativeMethods.RECT bounds, NativeMethods.RECT monitor) =>
        bounds.left <= monitor.left && bounds.top <= monitor.top
        && bounds.right >= monitor.right && bounds.bottom >= monitor.bottom;
}
