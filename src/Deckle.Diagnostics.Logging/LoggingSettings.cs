namespace Deckle.Diagnostics.Logging;

// Operational-log settings, persisted by LoggingSettingsService: producer-side
// admission gates plus the trace.jsonl opt-in. The LogWindow view filter is a
// separate, process-lifetime selection and never belongs here.
public sealed class LoggingSettings
{
    // Optional disk mirror of admitted operational observations. Its path is
    // fixed by the Logging module at diagnostics/trace.jsonl; telemetry settings
    // and storage overrides never redirect it.
    public bool ApplicationLogToDisk { get; set; } = false;

    // Every gate below ships off: routine per-loop detail drowns out everything
    // else, while milestones, warnings and errors always pass.

    // When false and an ambient capture loop is active, producers skip their
    // Verbose operational detail.
    public bool LogAmbientCaptureActivity { get; set; } = false;

    // When false and a transcription workflow is active, the transcription
    // providers skip their Verbose detail, heartbeat and per-utterance details
    // included.
    public bool LogTranscriptionActivity { get; set; } = false;

    // When false, the autocorrect producer skips its chatty Verbose stream (the
    // SurfaceChanged probe, the learning signals, the activity rollup). An
    // applied correction's Verbose detail (reason and lengths, never the word)
    // and injection failures remain. There is no capture window: the engine
    // runs continuously, so the gate applies whenever the toggle is off.
    public bool LogAutocorrectActivity { get; set; } = false;

    // When false, the Raw Input host does not collect its contact-frame rollup
    // and Trackpad does not emit per-gesture detail. Device presence,
    // lifecycle, incidents, warnings and recoveries remain admitted.
    public bool LogInputActivity { get; set; } = false;

    // When false, windowing probes skip their Verbose work. There is no capture
    // window, and the provider emits Verbose only, so off silences the whole
    // windowing channel.
    public bool LogWindowingActivity { get; set; } = false;

}
