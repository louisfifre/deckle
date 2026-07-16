using System.Collections.Generic;

namespace Deckle.Diagnostics;

// Feeds the live LogWindow surface. A passive ILogSink: the single
// DispatchEventListener builds each admitted EventEntry, then offers it here.
// This sink owns the boot-history buffer and the lazy attach of the actual UI
// surface. Wants rejects datasets at the projection boundary; user filtering
// then happens on the UI side, over the operational stream only.
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

    public bool Wants(EventEntry entry)
        => entry.Kind == ObservationKind.Operational;

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
