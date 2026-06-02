using System;
using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Transcription.Engine;

// ── IAsrBackend ──────────────────────────────────────────────────────────────
//
// Contract for an interchangeable ASR inference engine. Implementations live
// in child modules (Deckle.Transcription.Whisper today; Deckle.Transcription
// .Voxtral planned). The orchestrator (TranscriptionEngine in this module)
// holds one IAsrBackend instance, never touches a backend's internals, and
// drives it through the four method signatures below.
//
// "Backend" is borrowed from the established ML/AI vocabulary (llama.cpp,
// whisper.cpp, transformers all call swappable inference implementations
// "backends"). It extends the Deckle suffix vocabulary by tracked decision —
// see ADR 0005.
//
// Threading. Every method may be called from a non-UI thread. LoadModelAsync
// is called once per session (lazy, on first hotkey); UnloadModel can fire
// from a Timer; TranscribeAsync is called from the recording worker. The
// implementation owns its internal serialization — the orchestrator does
// not lock around the backend calls (lifetime aside).
public interface IAsrBackend : IDisposable
{
    // Stable identifier for telemetry and logs ("whisper", "voxtral", ...).
    // Not a display name — the Settings UI labels backends from its own
    // catalog.
    string Name { get; }

    // True once a model is loaded in memory and ready to transcribe. False
    // before the first LoadModelAsync and after each UnloadModel. Reading
    // this is the cheapest path the orchestrator has to decide whether to
    // pay the cold-load cost on the next hotkey.
    bool IsModelLoaded { get; }

    // Compute device that ended up serving the loaded model. Set by
    // LoadModelAsync; null when no model is loaded. The vocabulary is
    // backend-defined ("CPU", "Vulkan", "CUDA", "Metal", "ROCm", ...).
    // Surfaced in telemetry and the LogWindow header line.
    string? DetectedAccelerator { get; }

    // Loads the model file referenced by the host's TranscriptionSettings.
    // Synchronous wall time is captured in the result for the latency payload.
    // Failure paths return ModelLoadResult.Success=false with a stable
    // ErrorReason ("file_not_found", "init_failed", ...) — exceptions are
    // reserved for invariant breaches.
    Task<ModelLoadResult> LoadModelAsync(CancellationToken ct);

    // Frees the model and any GPU memory it holds. Idempotent — calling on
    // an already-unloaded backend is a no-op. Called by the idle timer and
    // by Dispose.
    void UnloadModel();

    // Runs inference on a 16-kHz mono float PCM buffer and returns the
    // transcribed segments. Behaviour notes:
    //
    //   • Segments stream through `segmentSink` as they are produced. The
    //     callback fires synchronously on the backend's inference thread —
    //     subscribers are responsible for marshaling. The same segments
    //     also appear in the final TranscriptionResult; the sink channel
    //     is for live UI/log updates, not for the consumer of the result.
    //   • `ct` cancels mid-inference; the backend wires it to whatever
    //     native abort hook it provides. Cancellation is observable as
    //     TranscriptionResult.Aborted=true rather than an exception, so
    //     the orchestrator can still claim partial segments already emitted.
    //   • Returns the full assembled text + per-segment metrics + wall-clock
    //     phase timings the orchestrator forwards to the latency payload.
    //   • `context` (optional) supplies model-agnostic priming for this call —
    //     used by the streaming socle to carry continuity across separate
    //     per-utterance calls. Null (the default) means "no priming beyond the
    //     backend's own configured settings", so the monolithic path and any
    //     existing caller are unaffected.
    Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null);
}

// ── TranscriptionContext ─────────────────────────────────────────────────────
//
// Optional per-call priming, model-agnostic by design so the contract outlives
// Whisper. The streaming pipeline builds it to carry continuity across the
// separate backend calls of consecutive utterances (Whisper has no cross-call
// context — carry_initial_prompt and no_context both stay WITHIN one call).
//
//   • PrimingText — prior text to prime the decoder with. The Whisper backend
//     maps it onto initial_prompt (overriding the configured stylistic prompt
//     for that call); a future backend may treat it as conversation history or
//     ignore it. Null/empty means no override.
public sealed record TranscriptionContext(string? PrimingText);

// ── ModelLoadResult ──────────────────────────────────────────────────────────
//
// Outcome of LoadModelAsync. Success carries the wall-clock load time so the
// orchestrator can populate the cold-load field in the latency payload; the
// detected accelerator surfaces in logs. Failure carries a stable reason
// string the orchestrator maps to a localized user message — the backend
// stays free of UI vocabulary.
public sealed record ModelLoadResult(
    bool Success,
    long LoadDurationMs,
    string? Accelerator,
    string? ErrorReason);

// ── TranscriptionResult ──────────────────────────────────────────────────────
//
// Final payload of a TranscribeAsync call. Carries the assembled segments
// (same instances streamed via segmentProgress), the joined full text the
// pipeline pushes to clipboard, and the wall-clock breakdown the latency
// payload needs.
//
//   • TotalDurationMs covers the entire backend call (setup + inference).
//   • InitDurationMs is the pre-inference setup window (mel computation,
//     GPU upload, etc.) — 0 when the backend cannot distinguish phases.
//   • VadDurationMs is the time spent inside VAD when enabled — 0 when
//     VAD is off or the backend has no VAD step.
//   • Aborted=true means cancellation or backend-internal abort (e.g. a
//     repetition guard) fired; the segments already produced are still
//     usable, the consumer decides whether to keep them.
//   • ResultCode is a backend-specific status surfaced for telemetry only.
//     0 means success; non-zero is interpretation-free here.
public sealed record TranscriptionResult(
    IReadOnlyList<TranscriptionSegment> Segments,
    string FullText,
    long TotalDurationMs,
    long InitDurationMs,
    long VadDurationMs,
    bool Aborted,
    int ResultCode);

// ── TranscriptionSegment ─────────────────────────────────────────────────────
//
// One segment emitted by the backend during inference. Timing in centiseconds
// since the start of the current TranscribeAsync call — matches whisper.cpp's
// native unit, kept as-is to avoid rounding noise. Confidence is the linear
// average token probability over text tokens (excludes timestamp tokens);
// NoSpeechProb is the per-segment probability that the audio is silence.
public readonly record struct TranscriptionSegment(
    string Text,
    long T0Cs,
    long T1Cs,
    float Confidence,
    float NoSpeechProb);
