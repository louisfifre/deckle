using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: explicit application cancellations. Capturing
// OperationCanceledException at sites where they are semantically interesting
// (Whisper engine, Vision capture, Llm rewrite, Ollama polling) answers "why
// did the operation stop" without guessing. Without this event, an OCE
// silently swallowed in a catch leaves no trace, and a chain of cascading
// cancellations is impossible to reconstruct after the fact. The primitive is
// strictly non-business and consumed by several modules with the same
// parameter set.
//
// Closed `operation` vocabulary (extend here if a new cancellation site
// emerges; no ad hoc operation on the call-site side):
//   "whisp-transcribe" — cancellation during transcription (warmup,
//                        VAD, whisper_run)
//   "file-transcription-batch" — cancellation of a file transcription batch
//   "whisp-record"     — cancellation during audio capture
//   "vision-capture"   — screen capture loop cancellation
//   "vision-sample"    — frame sampler cancellation
//   "llm-rewrite"      — Ollama rewrite cancellation (user timeout,
//                        restart engine)
//   "llm-warmup"       — cancellation during prolonged /api/ps polling
//   "llm-models"       — cancellation during an admin operation on the model
//                        list (delete, refresh)
//   "ambient-pipeline" — ambient push loop cancellation
//                        (Stop user, capture lost upstream, external
//                        Hue interference, DisposeAsync shutdown)
//
// Closed `reason` vocabulary:
//   "user"           — the user triggered cancellation (Hide, hotkey, stop
//                      button, surface close)
//   "timeout"        — a delay expired (REWRITE_HARD_CAP, etc.)
//   "shutdown"       — the app is closing
//   "hotkey"         — the user pressed the hold-to-talk hotkey to stop
//   "engine-restart" — the engine is restarting, cancelling in-flight
//                      operations
//   "upstream"       — the operation was cancelled because a previous step
//                      requested it (ct propagation)
//
// `age_ms` runs from operation start to the OCE; pass -1 when the call site
// has no time anchor.
[EventSource(Name = "Deckle-Cancellation")]
public sealed class DeckleCancellationSource : DeckleEventSource
{
    public static readonly DeckleCancellationSource Log = new();

    private DeckleCancellationSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtOperationCancelled = 1;

    // Verbose because one user Stop cascades into several OCEs on worker
    // threads. Keyword `Lifecycle` because the provider itself carries the
    // cancellation nature; no keyword bit is spent on it.
    [Event(EvtOperationCancelled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "operation cancelled | operation={0} | reason={1} | age_ms={2}")]
    public void OperationCancelled(string operation, string reason, int age_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtOperationCancelled, operation, reason, age_ms);
    }
}
