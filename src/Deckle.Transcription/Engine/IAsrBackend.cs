using System;
using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Transcription;

// ── IAsrBackend ──────────────────────────────────────────────────────────────
//
// Contract for an interchangeable ASR inference engine, implemented in child
// modules (Deckle.Transcription.Whisper). The orchestrator holds one instance
// and never touches its P/Invoke, native callbacks or C structs. "Backend" is
// the ML sense (llama.cpp, whisper.cpp): the swappable inference
// implementation, not a generic service or provider abstraction.
//
// Threading. Every method may be called from a non-UI thread: LoadModelAsync
// from a thread-pool prime, UnloadModel from the idle timer, TranscribeAsync
// from the worker or a thread-pool task. The implementation owns its internal
// serialization; the orchestrator does not lock around backend calls
// (lifetime aside).
public interface IAsrBackend : IDisposable
{
    // Stable identifier for telemetry and logs ("whisper", ...). Not a display
    // name: the Settings UI labels backends from its own catalog.
    string Name { get; }

    bool IsModelLoaded { get; }

    // Backend-defined vocabulary ("CPU", "Vulkan", "CUDA", ...); null when no
    // model is loaded.
    string? DetectedAccelerator { get; }

    // Failure returns Success=false with a stable ErrorReason
    // ("file_not_found", "init_failed", ...); exceptions are reserved for
    // invariant breaches.
    Task<ModelLoadResult> LoadModelAsync(CancellationToken ct);

    // Idempotent. Called by the idle timer and by Dispose.
    void UnloadModel();

    // Runs inference on a 16 kHz mono float PCM buffer.
    //
    //   • `segmentSink` fires synchronously on the backend's inference thread,
    //     so subscribers marshal. The same segments also appear in the final
    //     TranscriptionResult; the sink is for live UI/log updates.
    //   • `ct` cancels mid-inference through the backend's native abort hook.
    //     Cancellation shows as TranscriptionResult.Aborted=true, not as an
    //     exception, so the caller can still keep the partial segments.
    //   • `context` carries per-call priming, used by the streaming pipeline
    //     to keep continuity across per-utterance calls. Null means no priming
    //     beyond the backend's configured settings.
    Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null);
}

// ── TranscriptionContext ─────────────────────────────────────────────────────
//
// Optional per-call priming, model-agnostic so the contract outlives Whisper.
// The streaming pipeline builds it to carry continuity across the separate
// backend calls of consecutive utterances.
//
//   • PrimingText — the Whisper backend maps it onto initial_prompt,
//     overriding the configured stylistic prompt for that call. Null keeps the
//     configured prompt; empty explicitly suppresses priming for an
//     independent file.
//   • ContinuesPreviousCall — whether this call may build on what the backend
//     decoded in its previous call. A backend can keep that text in its own
//     state (Whisper does), so only the caller knows where a take starts: false
//     makes this call start from nothing but its PrimingText. False by default,
//     so a standalone call, the warm-up and the first call of a take never
//     inherit text from another take or from the warm-up.
//   • EmitPreamble — whether this call logs its configuration preamble (the
//     "Transcribing" milestone, the resolved params, the prompt). False for
//     every streaming utterance after the first, so a take logs them once.
//     Observability only; it changes nothing the backend computes.
//   • TimelineOffsetSec — where this call's audio sits on the whole
//     recording's timeline. The backend adds it to the segment t0/t1 it logs,
//     so a streaming segment reads its true position in the take.
public sealed record TranscriptionContext(
    string? PrimingText,
    bool EmitPreamble = true,
    double TimelineOffsetSec = 0,
    bool ContinuesPreviousCall = false);

// ── ModelLoadResult ──────────────────────────────────────────────────────────
//
// Failure carries a stable reason string the orchestrator maps to a localized
// user message, so the backend stays free of UI vocabulary.
public sealed record ModelLoadResult(
    bool Success,
    long LoadDurationMs,
    string? Accelerator,
    string? ErrorReason);

// ── TranscriptionResult ──────────────────────────────────────────────────────
//
//   • Segments are the same segments streamed through segmentSink.
//   • TotalDurationMs covers the entire backend call (setup + inference).
//   • InitDurationMs is the pre-inference setup window (mel computation,
//     GPU upload, etc.) — 0 when the backend cannot distinguish phases.
//   • Aborted=true means cancellation or backend-internal abort (e.g. a
//     repetition guard) fired; the segments already produced are still
//     usable, the consumer decides whether to keep them.
//   • ResultCode is a backend-specific status; 0 means success.
//   • ErrorReason is a stable reason for a non-zero ResultCode
//     ("no_context", "native_error", ...), null on success. The backend logs
//     the failure only as Verbose detail, so the engine's own failure event
//     carries this reason as the visible cause.
public sealed record TranscriptionResult(
    IReadOnlyList<TranscriptionSegment> Segments,
    string FullText,
    long TotalDurationMs,
    long InitDurationMs,
    bool Aborted,
    int ResultCode,
    string? ErrorReason = null);

// ── TranscriptionSegment ─────────────────────────────────────────────────────
//
// One segment emitted by the backend during inference. Timing in centiseconds
// since the start of the current TranscribeAsync call — matches whisper.cpp's
// native unit, kept as-is to avoid rounding noise. Confidence is the linear
// average token probability over text tokens (excludes timestamp tokens);
// NoSpeechProb is the per-segment probability that the audio is silence.
// TextTokens counts the text tokens Confidence averages over, and
// MinTokenProbability is the lowest of them; a backend that cannot report them
// leaves the defaults (0 tokens, probability 1), which read as "unknown".
public readonly record struct TranscriptionSegment(
    string Text,
    long T0Cs,
    long T1Cs,
    float Confidence,
    float NoSpeechProb,
    int TextTokens = 0,
    float MinTokenProbability = 1f);
