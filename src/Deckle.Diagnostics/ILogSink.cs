namespace Deckle.Diagnostics;

// A passive consumer of the single dispatch pipeline. The one EventListener
// (DispatchEventListener) subscribes to the whole Deckle-* family, builds each
// admitted EventEntry ONCE, then offers it to
// every registered sink. A sink never subscribes to an EventSource itself — it
// only decides whether it wants an already-built entry and how to write it.
//
// Two-method split. `Wants` is the sink's own selection — event name, level,
// consent gate, destination-specific projection. `Write` performs the side
// effect (append a JSONL line, push to the live window, flash the HUD). The
// dispatcher calls Write only when Wants returned true, so a sink expresses its
// routing in one place and never re-checks it in Write.
//
// Why this shape. Before the dispatch refonte every sink was its own
// EventListener: nine parallel subscriptions to Deckle-* and the EventEntry
// rebuilt by each listener. Collapsing subscription and build into the
// dispatcher leaves admission with producers and projection with sinks.
//
// Threading. The dispatcher invokes Wants/Write on the emitting thread,
// possibly concurrently for different events. A sink that touches shared state
// (a file, a ring buffer, a UI surface) guards it itself, exactly as the former
// listeners did. A sink must never throw out of Wants or Write — the dispatcher
// swallows exceptions defensively, but the contract is that a sink failure is
// contained, never propagated to the emitter.
public interface ILogSink
{
    // True when this sink wants the entry written. Pure decision, no side
    // effect: event name, level, keywords, consent gate, payload projection.
    bool Wants(EventEntry entry);

    // Performs the sink's side effect for an entry Wants accepted.
    void Write(EventEntry entry);
}
