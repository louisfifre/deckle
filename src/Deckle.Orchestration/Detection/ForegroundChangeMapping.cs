using Deckle.Shell.Foreground;

namespace Deckle.Orchestration;

// Pure translation from the shared watcher's snapshot (ForegroundState) to
// this module's ForegroundChange. No Win32 here: the executable path is
// resolved by the caller and passed in, so the mapping is tested alone.
internal static class ForegroundChangeMapping
{
    /// <summary>
    /// Whether a watcher snapshot is worth raising, given the last change
    /// raised: another window came forward, or the same window changed its
    /// fullscreen verdict or its monitor. The watcher also publishes rect-only
    /// moves, which say nothing new here. A snapshot without a process (no
    /// foreground, or the system did not name it) is never raised.
    /// </summary>
    internal static bool IsNews(ForegroundChange? previous, ForegroundState next)
    {
        if (next.Hwnd == IntPtr.Zero || next.ProcessId == 0) return false;
        return previous is null
            || previous.WindowHandle != next.Hwnd
            || previous.ProcessId != (int)next.ProcessId
            || previous.IsFullscreen != next.IsFullscreen
            || !string.Equals(previous.MonitorDeviceName, NullIfEmpty(next.MonitorDeviceName), StringComparison.Ordinal);
    }

    /// <summary>
    /// The change raised for <paramref name="state"/>. Fullscreen is any stage
    /// the watcher detects (shell notification state or window geometry):
    /// IForegroundMonitor asks for "the fullscreen verdict", and the watcher's
    /// verdict is exactly <see cref="ForegroundState.IsFullscreen"/>. The
    /// watcher's empty strings (unknown process name, unknown monitor) become
    /// null, the "unknown" of this contract.
    /// </summary>
    internal static ForegroundChange ToChange(ForegroundState state, string? executablePath) => new(
        WindowHandle: state.Hwnd,
        ProcessId: (int)state.ProcessId,
        ProcessName: NullIfEmpty(state.ProcessName),
        ExecutablePath: NullIfEmpty(executablePath),
        IsFullscreen: state.IsFullscreen,
        MonitorDeviceName: NullIfEmpty(state.MonitorDeviceName));

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
