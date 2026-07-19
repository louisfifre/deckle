using System;
using System.Threading;

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

    private readonly EventEntry?[] _buffer = new EventEntry?[BufferCapacity];
    private readonly object _lock = new();
    private ILogWindowSink[] _sinkSnapshot = Array.Empty<ILogWindowSink>();
    private int _bufferStart;
    private int _bufferCount;

    public bool Wants(EventEntry entry)
        => entry.Kind == ObservationKind.Operational;

    public void Write(EventEntry entry)
    {
        ILogWindowSink[] snapshot;
        lock (_lock)
        {
            int writeIndex = (_bufferStart + _bufferCount) % BufferCapacity;
            _buffer[writeIndex] = entry;

            if (_bufferCount < BufferCapacity)
            {
                _bufferCount++;
            }
            else
            {
                // The write replaced the oldest slot; advance the logical
                // start so replay remains chronological.
                _bufferStart = (_bufferStart + 1) % BufferCapacity;
            }

            // Capture the already-published array while the replay boundary is
            // closed. Reading it after releasing the lock would let AttachSink
            // replay this entry and publish itself before the live snapshot,
            // delivering the same event twice.
            snapshot = Volatile.Read(ref _sinkSnapshot);
        }

        foreach (var sink in snapshot)
        {
            try { sink.Write(entry); }
            catch { /* A UI sink must never crash the dispatcher. */ }
        }
    }

    // Attaches a UI sink and replays the buffered history since boot. Replay and
    // snapshot publication share the buffer lock: an event arriving during the
    // handoff waits, then follows the replay through the live path. History is
    // therefore chronological, with no lost or duplicated boundary event.
    public void AttachSink(ILogWindowSink sink)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        lock (_lock)
        {
            for (int i = 0; i < _bufferCount; i++)
            {
                EventEntry entry = _buffer[(_bufferStart + i) % BufferCapacity]!;
                try { sink.Write(entry); }
                catch { /* A UI sink must never crash the sink. */ }
            }

            ILogWindowSink[] current = Volatile.Read(ref _sinkSnapshot);
            var next = new ILogWindowSink[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = sink;
            Volatile.Write(ref _sinkSnapshot, next);
        }
    }

    public void DetachSink(ILogWindowSink sink)
    {
        lock (_lock)
        {
            ILogWindowSink[] current = Volatile.Read(ref _sinkSnapshot);
            int index = Array.IndexOf(current, sink);
            if (index < 0) return;

            if (current.Length == 1)
            {
                Volatile.Write(ref _sinkSnapshot, Array.Empty<ILogWindowSink>());
                return;
            }

            var next = new ILogWindowSink[current.Length - 1];
            if (index > 0) Array.Copy(current, 0, next, 0, index);
            if (index < current.Length - 1)
                Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            Volatile.Write(ref _sinkSnapshot, next);
        }
    }

    // Clears only the process-memory replay ring. Attached sinks keep receiving
    // future entries, and persistent sinks such as diagnostics/app.jsonl are
    // intentionally untouched.
    public void ClearBuffer()
    {
        lock (_lock)
        {
            Array.Clear(_buffer);
            _bufferStart = 0;
            _bufferCount = 0;
        }
    }
}
