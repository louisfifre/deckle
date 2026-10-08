namespace Deckle.Orchestration;

// What the runtime can notice right now. The page reads it to decide whether
// to advise turning on "Start elevated".
// - ProcessTraceAvailable: WMI start events flow (Deckle is elevated).
// - ForegroundMonitorPresent: a real foreground service is plugged in.
public sealed record DetectionStatus(bool ProcessTraceAvailable, bool ForegroundMonitorPresent);
