namespace Deckle.Diagnostics.Logging;

// User-facing settings of the live LogWindow. POCO with per-module
// persistence — loaded / saved by LoggingSettingsService.
//
// These settings are runtime emission gates. The LogWindow display lens and
// app.jsonl persistence lens are independent filter selections with different
// lifetime policies; neither belongs in this emission-gate POCO.
public sealed class LoggingSettings
{
    // Optional disk mirror of admitted operational observations. Its path is
    // fixed by the Logging module at diagnostics/app.jsonl; telemetry settings
    // and storage overrides never redirect it.
    public bool ApplicationLogToDisk { get; set; } = false;

    // When false and an ambient capture loop is active, producers skip their
    // Verbose operational detail. Informational milestones still pass.
    // Off-by-default for the noisy capture path.
    public bool LogAmbientCaptureActivity { get; set; } = false;

    // When false and a streaming transcription pipeline is active, Verbose
    // events from the transcription providers are not produced — including
    // the 1 Hz heartbeat and per-utterance details. Bracketing milestones still pass:
    // StreamingPipelineStarted and StreamingDrained tell the user the take
    // ran. Off-by-default — same quiet-by-default posture as the ambient gate.
    public bool LogTranscriptionActivity { get; set; } = false;

    // When false (the default), the autocorrect producer skips its chatty
    // Verbose stream — the per-focus
    // SurfaceChanged probe, the learning signals and the 30 s activity rollup.
    // What remains is only the edits: an applied correction's Verbose detail
    // (reason and lengths, never the word) plus its milestone, and any
    // injection failure. No heartbeat — it is meaningless for a keystroke-driven
    // subsystem. Unlike the ambient and streaming gates there is no capture
    // window: the engine runs continuously, so admission applies whenever the
    // toggle is off. Off-by-default — same quiet-by-default posture as its
    // siblings.
    public bool LogAutocorrectActivity { get; set; } = false;

    // When false (the default), windowing probes skip their Verbose work — window
    // placement and DPI, overlay stacking, popup anchoring, z-order checks,
    // resize frames and first-open construction timings. Unlike the ambient
    // and streaming gates there is no capture window, and unlike autocorrect
    // there is no surviving sub-stream: the provider emits Verbose only, so
    // off means the windowing channel is fully silent. On surfaces everything,
    // for a placement / multi-monitor / resize-lag deep dive. Off-by-default —
    // same quiet-by-default posture as its siblings.
    public bool LogWindowingActivity { get; set; } = false;

}
