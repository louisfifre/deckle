using Deckle.Shell.Foreground;

namespace Deckle.Shell.TaskbarCover;

// When the foreground stands a band down: a fullscreen window on its monitor, or
// its own taskbar taking focus. Pure: no Win32 call, no state.
public static class CoverSuppression
{
    // Only a fullscreen window on the band's own monitor suppresses it: on
    // another monitor the band hides nothing of that app. Both stages are
    // compared the same way. The notification-state stage is global by
    // nature (the shell reports it for the session, not for a monitor), so
    // the foreground window's monitor is the best available discriminator.
    public static bool Applies(ForegroundState state, IntPtr bandMonitor)
        => state.IsFullscreen && state.Monitor == bandMonitor;

    // The band's own taskbar is the foreground window, as after Windows + T or
    // the Windows key of the "Windows key shows the taskbar" setting. The band
    // steps aside: the person asked for the taskbar, and a band over it would
    // hide it. It comes back when the foreground moves on. Another monitor's
    // taskbar does not count, its band hides nothing of this one.
    public static bool TaskbarFocused(ForegroundState state, IntPtr bandTaskbar)
        => bandTaskbar != IntPtr.Zero && state.Hwnd == bandTaskbar;
}
