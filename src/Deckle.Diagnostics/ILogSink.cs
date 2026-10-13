namespace Deckle.Diagnostics;

// A passive consumer of the single dispatch pipeline. The one EventListener
// (DispatchEventListener) builds each EventEntry once, then offers it to every
// registered sink. A sink never subscribes to an EventSource itself — it only
// decides whether it wants an already-built entry and how to write it.
//
// The dispatcher calls Write only when Wants returned true, so a sink
// expresses its routing in one place and never re-checks it in Write.
//
// Threading. The dispatcher invokes Wants/Write on the emitting thread,
// possibly concurrently for different events. A purely in-memory sink guards
// its own shared state. A destination doing blocking I/O implements
// IFlushableLogSink and hands the entry to its bounded writer instead. A sink
// must never throw out of Wants or Write — the dispatcher swallows exceptions
// defensively, but the contract is that a sink failure is contained, never
// propagated to the emitter.
public interface ILogSink
{
    // Pure decision, no side effect.
    bool Wants(EventEntry entry);

    void Write(EventEntry entry);
}
