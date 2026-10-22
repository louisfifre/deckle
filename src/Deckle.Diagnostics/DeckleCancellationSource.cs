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
// The closed `operation` and `reason` vocabularies are the constants of
// CancelledOperations and CancellationReasons below: a call site passes a
// constant, never a literal, so a typo cannot open a new value that no
// listener greps for. Extend there when a new cancellation site emerges.
//
// `age_ms` runs from operation start to the OCE; pass -1 when the call site
// has no time anchor.
[EventSource(Name = "Deckle-Cancellation")]
public sealed class DeckleCancellationSource : DeckleEventSource
{
    public static readonly DeckleCancellationSource Log = new();

    private DeckleCancellationSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
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

// Closed `operation` vocabulary of OperationCancelled.
public static class CancelledOperations
{
    // Transcription (warmup, VAD, whisper_run).
    public const string WhispTranscribe = "whisp-transcribe";

    // A file transcription batch.
    public const string FileTranscriptionBatch = "file-transcription-batch";

    // Audio capture.
    public const string WhispRecord = "whisp-record";

    // Screen capture loop.
    public const string VisionCapture = "vision-capture";

    // Frame sampler.
    public const string VisionSample = "vision-sample";

    // Ollama rewrite as a whole (user timeout, engine restart).
    public const string LlmRewrite = "llm-rewrite";

    // One paragraph of a rewrite; the rewrite itself may go on with the next.
    public const string ParagraphRewrite = "paragraph-rewrite";

    // One sentence of a paragraph rewrite.
    public const string SentenceRewrite = "sentence-rewrite";

    // Prolonged /api/ps polling while a generation waits for its model.
    public const string LlmWarmup = "llm-warmup";

    // An admin operation on the model list (delete, refresh).
    public const string LlmModels = "llm-models";

    // Ambient push loop (user Stop, capture lost upstream, external Hue
    // interference, DisposeAsync shutdown).
    public const string AmbientPipeline = "ambient-pipeline";

    // One reconciliation pass of the Anytype backend.
    public const string AnytypeReconcile = "anytype-reconcile";

    // Waiting for the lease that serializes Anytype reconciliations.
    public const string AnytypeReconcileLease = "anytype-reconcile-lease";

    // The supervision loop of the Anytype backend process.
    public const string AnytypeSupervise = "anytype-supervise";

    // One MCP tool call in flight.
    public const string McpToolCall = "mcp-tool-call";

    // Draining in-flight MCP requests while the HTTP host stops.
    public const string McpHostDrain = "mcp-host-drain";
}

// Closed `reason` vocabulary of OperationCancelled.
public static class CancellationReasons
{
    // The user triggered cancellation (Hide, stop button, surface close).
    public const string User = "user";

    // A delay expired (REWRITE_HARD_CAP, etc.).
    public const string Timeout = "timeout";

    // The app is closing.
    public const string Shutdown = "shutdown";

    // The user pressed the hold-to-talk hotkey to stop.
    public const string Hotkey = "hotkey";

    // The engine is restarting, cancelling in-flight operations.
    public const string EngineRestart = "engine-restart";

    // A previous step requested it (token propagation).
    public const string Upstream = "upstream";

    // A newer request of the same kind replaced this one.
    public const string Superseded = "superseded";

    // The work this operation served finished, so the operation was stopped
    // on purpose: Ollama's /api/ps poll is cancelled as soon as the
    // generation it watches settles, which is the normal end, not a failure.
    public const string Completed = "completed";
}
