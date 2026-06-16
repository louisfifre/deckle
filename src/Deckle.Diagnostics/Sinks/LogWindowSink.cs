using System.Collections.Generic;

namespace Deckle.Diagnostics;

// Feeds the live LogWindow surface. A passive ILogSink: the single
// DispatchEventListener gates and builds each EventEntry, then offers it here.
// This sink owns the boot-history buffer and the lazy attach of the actual UI
// surface; it does not itself decide what is silenced — that is the
// dispatcher's central gate, applied before this sink is ever called.
//
// Wants is unconditional. The LogWindow takes the whole Deckle-* family with no
// masking at this layer; user filtering (All / Activity / Alerts) happens on
// the UI sink side, over the live window. The only events this sink never sees
// are the ones the central capture gate already dropped upstream — which is
// exactly the invariant: the window and app.jsonl observe the same gated
// stream.
//
// Buffer for lazy LogWindow. The LogWindow is created lazily on first user
// open; events emitted during boot must be visible as soon as it opens. The
// sink keeps a fixed-capacity ring (5000) and replays it in full when a UI sink
// attaches through `AttachSink`. Buffering starts the moment this sink is
// registered with the dispatcher, so the history covers from boot to first
// open.
//
// Threading. The dispatcher invokes Write on the emitting thread. The
// ILogWindowSink implementation marshals to the UI thread if it needs to (e.g.
// via DispatcherQueue).
public sealed class LogWindowSink : ILogSink
{
    private const int BufferCapacity = 5000;

    private readonly List<ILogWindowSink> _sinks = new();
    private readonly List<EventEntry> _buffer = new(capacity: BufferCapacity);
    private readonly object _lock = new();

    public bool Wants(EventEntry entry) => true;

    public void Write(EventEntry entry)
    {
        ILogWindowSink[] snapshot;
        lock (_lock)
        {
            // Ring: bound the buffer so it does not grow indefinitely on long
            // sessions. When the cap is exceeded, discard the oldest entry —
            // same posture as `LogWindow` on the UI side (cap 5000 in
            // `_entries`). Capacity matches so opening replay fills exactly the
            // window the user will see.
            _buffer.Add(entry);
            if (_buffer.Count > BufferCapacity) _buffer.RemoveAt(0);
            snapshot = _sinks.ToArray();
        }

        foreach (var sink in snapshot)
        {
            try { sink.Write(entry); }
            catch { /* A UI sink must never crash the dispatcher. */ }
        }
    }

    // Attaches a UI sink and replays the buffered history since boot. Replay is
    // done under the buffer lock so no event can slip between snapshot copy and
    // sink registration; an event arriving during replay is captured in the
    // live path, never lost or duplicated.
    public void AttachSink(ILogWindowSink sink)
    {
        EventEntry[] replay;
        lock (_lock)
        {
            replay = _buffer.ToArray();
            _sinks.Add(sink);
        }
        foreach (var entry in replay)
        {
            try { sink.Write(entry); }
            catch { /* A UI sink must never crash the sink. */ }
        }
    }

    public void DetachSink(ILogWindowSink sink)
    {
        lock (_lock) _sinks.Remove(sink);
    }
}
