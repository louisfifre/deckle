using Deckle.Core;

namespace Deckle.Shell.Foreground;

// How the foreground window reads as fullscreen, in the order the watcher
// tests it: the shell's own notification state first (D3D exclusive,
// presentation mode, busy), then the window's visual bounds covering its
// whole monitor (borderless fullscreen, F11).
public enum FullscreenStage
{
    None,
    NotificationState,
    Geometry,
}

// One immutable snapshot of the foreground window: who it is (window,
// process id, process name as Process.ProcessName spells it), where it sits
// (its monitor handle, the monitor rect and work area in physical pixels)
// and whether it reads as fullscreen. Swapped by reference, so any thread
// reads a consistent set of fields.
public sealed record ForegroundState(
    IntPtr Hwnd, uint ProcessId, string ProcessName,
    IntPtr Monitor, NativeMethods.RECT MonitorRect, NativeMethods.RECT WorkRect,
    FullscreenStage Fullscreen)
{
    public bool IsFullscreen => Fullscreen != FullscreenStage.None;

    // No foreground known: before Start, after Stop, or a foreground the
    // system did not name.
    public static readonly ForegroundState None =
        new(IntPtr.Zero, 0, "", IntPtr.Zero, default, default, FullscreenStage.None);
}
