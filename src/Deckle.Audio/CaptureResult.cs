using Deckle.Audio;

namespace Deckle.Audio;

// Outcome of a Record() call — closed enum, no localized strings here.
//   Completed  — normal user-driven Stop OR cancellation token fired.
//                Audio is in Pcm.
//   CapHit     — the host's MaxRecordingDurationSeconds was reached. Audio
//                captured up to the cap is in Pcm; the pipeline continues
//                normally.
//   MicError   — waveInOpen failed. Pcm is empty. MmsysErr carries the raw
//                MMSYSERR value for the orchestrator to localize.
//   Cancelled  — never produced by MicrophoneCapture: a Stop through the
//                token returns Completed with the audio recorded so far.
public enum CaptureOutcome
{
    Completed,
    CapHit,
    MicError,
    Cancelled,
}

// Returned from MicrophoneCapture.Record. Pcm is float[-1, 1] mono 16 kHz
// (PCM16 normalized). Telemetry is null only when the recording was too
// short to produce any RMS sample (or on MicError).
//
// DrainDuration brackets the post-Stop drain phase, so the pipeline timings
// can isolate it from the rest of the stop-to-pipeline cost.
//
// MmsysErr is 0 unless Outcome == MicError.
public readonly record struct CaptureResult(
    float[] Pcm,
    MicrophoneTelemetryPayload? Telemetry,
    CaptureOutcome Outcome,
    System.TimeSpan DrainDuration,
    uint MmsysErr);
