namespace Deckle.Orchestration;

// What the runtime can notice right now. The page reads it to decide whether
// to tell the person that apps are noticed only when they come to the front.
// - ProcessTraceAvailable: WMI start events flow (Deckle is elevated).
// - ForegroundMonitorPresent: a real foreground service is plugged in.
public sealed record DetectionStatus(bool ProcessTraceAvailable, bool ForegroundMonitorPresent)
{
    // Without the process trace a listed app is noticed when it reaches the
    // foreground (or is already running), never when it starts in the
    // background: a game that never takes focus is missed. "Start elevated"
    // restores the instant detection.
    public bool NoticesOnlyAtForeground => !ProcessTraceAvailable;
}
