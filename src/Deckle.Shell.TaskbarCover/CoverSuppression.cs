using Deckle.Shell.Foreground;

namespace Deckle.Shell.TaskbarCover;

// When a fullscreen foreground stands the band down. Pure: no Win32 call,
// no state.
public static class CoverSuppression
{
    // Only a fullscreen window on the band's own monitor suppresses it: on
    // another monitor the band hides nothing of that app. Both stages are
    // compared the same way. The notification-state stage is global by
    // nature (the shell reports it for the session, not for a monitor), so
    // the foreground window's monitor is the best available discriminator.
    public static bool Applies(ForegroundState state, IntPtr bandMonitor)
        => state.IsFullscreen && state.Monitor == bandMonitor;
}
