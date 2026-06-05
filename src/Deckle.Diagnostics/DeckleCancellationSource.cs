using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: explicit application cancellations. Capturing
// OperationCanceledException at sites where they are semantically interesting
// (Whisper engine, Vision capture, Llm rewrite, Ollama polling) answers "why
// did the operation stop" without guessing: user, timeout, shutdown, hotkey,
// engine restart, upstream propagation. Without this cross-cutting event, an
// OCE silently swallowed in a catch leaves no trace, and a chain of cascading
// cancellations is impossible to reconstruct after the fact. The primitive is
// strictly non-business and consumed by several modules with the same parameter
// set: promotion to cross-cutting sub-provider under the two-clause criterion
// in `reference--eventsource-convention--1.2.md`
// §*Cross-cutting sub-providers*.
//
// Closed `operation` vocabulary (extend here if a new cancellation site
// emerges; no ad hoc operation on the call-site side):
//   "whisp-transcribe" — cancellation during transcription (warmup,
//                        VAD, whisper_run)
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
// `age_ms` is computed through Stopwatch.ElapsedMilliseconds between operation
// start and OCE throw when a local Stopwatch is available at the call site.
// When the age is not measurable (no Stopwatch in scope, operation without a
// time anchor), pass -1: it is explicit and grep-able.
[EventSource(Name = "Deckle.Diagnostics.Cancellation")]
public sealed class DeckleCancellationSource : DeckleEventSource
{
    public static readonly DeckleCancellationSource Log = new();

    private DeckleCancellationSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtOperationCancelled = 1;

    // Emitted on every site that catches an OperationCanceledException (or
    // TaskCanceledException, which inherits from it) on an application
    // operation whose cancellation carries intent. Verbose because
    // cancellations are frequent by nature (a typical user Stop will trigger
    // several cascading OCEs on worker threads) and grep-ability goes through
    // typed parameters rather than level. Keyword `Lifecycle` because there is
    // no dedicated `Cancellation` keyword: the "cancellation" nature is
    // carried by the provider itself (Deckle.Diagnostics.Cancellation), not by
    // an additional keyword bit.
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
