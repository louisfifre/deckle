namespace Deckle.Orchestration;

// What this module needs from the shared foreground service. The module never
// detects the foreground itself (no WinEvent hook here): WatcherForegroundMonitor
// adapts the shared ForegroundWatcher (Deckle.Shell.Foreground) to this
// interface; NullForegroundMonitor stands in for tests.
//
// Contract the implementation must honour:
// - Raise ForegroundChanged on EVENT_SYSTEM_FOREGROUND, and again whenever the
//   fullscreen verdict of the current foreground window changes (F11 in place).
// - ExecutablePath: resolved with OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
//   + QueryFullProcessImageNameW; null when access is denied.
// - ProcessName: the executable file stem (no directory, no ".exe").
// - MonitorDeviceName: the GDI device name of the window's monitor
//   (MONITORINFOEX.szDevice, e.g. "\\.\DISPLAY1").
// - Current: the last change raised, or null before the first one.
// - The event may be raised on any thread; subscribers must not assume the UI thread.
public interface IForegroundMonitor
{
    event Action<ForegroundChange>? ForegroundChanged;

    ForegroundChange? Current { get; }
}
