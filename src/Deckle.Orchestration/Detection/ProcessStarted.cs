namespace Deckle.Orchestration;

// A process that just started. ProcessName is the exe stem; ExecutablePath is
// null when the image path could not be read (protected or already gone).
public sealed record ProcessStarted(int ProcessId, string ProcessName, string? ExecutablePath);
