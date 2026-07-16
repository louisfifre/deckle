using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// ── TranscriptionActivityScope ─────────────────────────────────────────────
//
// Process-wide activity state for a complete transcription workflow: dictation
// (monolithic or streaming) and file transcription, including final delivery.
// Sister to AmbientCaptureGate, same shape, same rationale.
//
// Consumption. The gate is consulted by the LogWindow / app.jsonl Verbose drop
// filter wired in App. The filter combines this gate with the user toggle
// LoggingSettings.LogTranscriptionActivity to decide whether a Verbose
// emission from the Deckle.Whisp provider should land in the live or persistent
// journal. While the gate is open (streaming pipeline active) AND the toggle is
// off, Whisp Verbose events are silenced — the 1 Hz heartbeat and the per-
// utterance details. Non-Verbose levels and out-of-streaming emissions are
// unaffected.
//
// Emission. Pure shared state; no event. The transitions are bracketed by
// DeckleWhispSource.StreamingPipelineStarted (Info) and the existing
// StreamingDrained recap (Info), which carry the visible jalons.
//
// Threading. `volatile` for cross-thread visibility without a lock — the
// streaming pipeline flips the gate from the producer's task scheduling, and
// each emission reads the value without synchronization. Races are benign.
public static class TranscriptionActivityGate
{
    public static bool IsActive
        => OperationalLogAdmission.IsActive(OperationalLogActivity.Transcription);

    public static void SetActive(bool active)
        => OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, active);
}

// `using var _ = TranscriptionActivityScope.Open();` encloses the whole worker,
// so an early return or exception can never leave the activity stuck open.
public readonly struct TranscriptionActivityScope : System.IDisposable
{
    public static TranscriptionActivityScope Open()
    {
        TranscriptionActivityGate.SetActive(true);
        return default;
    }

    public void Dispose() => TranscriptionActivityGate.SetActive(false);
}
