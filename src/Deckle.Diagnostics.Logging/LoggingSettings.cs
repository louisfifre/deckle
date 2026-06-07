namespace Deckle.Diagnostics.Logging;

// User-facing settings of the live LogWindow. POCO with per-module
// persistence — loaded / saved by LoggingSettingsService.
//
// Minimal layout: a single gate for now, dedicated to runtime filtering of
// ambient capture loop noise and the All / Activity / Alerts level projection
// shared between LogWindow and app.jsonl. ApplicationLogToDisk lives on the
// TelemetrySettings side (Diagnostics.Telemetry) because it is a disk
// persistence gate, not an emission gate.
public sealed class LoggingSettings
{
    // When false and an ambient capture loop is active, Verbose events
    // from the ambient providers are dropped by the live LogWindow
    // listener and the app.jsonl predicate. Reproduces the legacy
    // TelemetryService._captureActive filter without re-introducing a
    // central hub. Off-by-default — matches the user's quiet-by-
    // default preference for the noisy capture path.
    public bool LogAmbientCaptureActivity { get; set; } = false;

    // When false and a streaming transcription pipeline is active, Verbose
    // events from the Deckle.Whisp provider are dropped — the 1 Hz heartbeat
    // and the per-utterance details. Bracketing milestones (Info) still pass:
    // StreamingPipelineStarted and StreamingDrained tell the user the take
    // ran. Off-by-default — same quiet-by-default posture as the ambient gate.
    public bool LogStreamingTranscriptionActivity { get; set; } = false;

    // Current journal projection. Search text stays UI-local, but the
    // level-family selector is persisted and reused by app.jsonl so the
    // disk journal follows the same broad visibility mode for new events.
    public LogWindowVisibilityMode LogWindowVisibilityMode { get; set; } = LogWindowVisibilityMode.All;
}
