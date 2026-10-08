namespace Deckle.Orchestration;

// The window that just came to the foreground, as the shared foreground
// service reports it. See IForegroundMonitor for what each field must hold.
public sealed record ForegroundChange(
    nint WindowHandle,
    int ProcessId,
    string? ProcessName,
    string? ExecutablePath,
    bool IsFullscreen,
    string? MonitorDeviceName);
