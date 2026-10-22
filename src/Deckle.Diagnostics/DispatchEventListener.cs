using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Threading;

namespace Deckle.Diagnostics;

// The single EventListener of the observability pillar. It is the only type
// that subscribes to the Deckle-* EventSource family; every consumer is a
// passive ILogSink registered here. For each emission the dispatcher:
//
//   1. builds the EventEntry ONCE, including its Operational/Dataset kind;
//   2. fans it out to every sink whose Wants accepted it.
//
// Admission is deliberately absent here. Activity policies are evaluated by
// producers before log-only work; the dispatcher is only the shared fan-out
// boundary. Stream routing remains per-sink through EventEntry.Kind.
//
// Boot replay. Sinks register in phases (local files first, the operational
// sinks after settings migration, telemetry last), so the first events of a
// run would reach only the sinks that existed when they fired. Until
// CompleteBoot, every built entry is also kept in a bounded ring; a sink
// registered with replayBoot receives that history before its first live
// entry. The ring keeps the oldest entries, because the start of a run is
// what a late sink has missed, and reports what it dropped past capacity.
//
// Sink faults. A sink that throws is contained, its failure never reaches the
// emitter, but it is not silent either: the first failure opens one incident
// on Deckle-Diagnostics, the attempts in between are only counted, and the
// next successful write closes it. Those reports are emitted from inside the
// fan-out, so they dispatch back through this listener; a thread-static depth
// stops a nested dispatch from reporting in turn.
//
// Threading. OnEventWritten fires on the emitting thread. Sink mutations build
// and publish an immutable array under a short lock; live dispatch only reads
// that snapshot. While boot replay is active the snapshot is read under the
// same lock as the ring append, so an entry is either replayed to a new sink
// or delivered live to it, never both and never neither.
public sealed class DispatchEventListener : EventListener
{
    public const int BootBufferCapacity = 2048;

    private readonly List<SinkSlot> _slots = new();
    private readonly object _sinksLock = new();
    private SinkSlot[] _snapshot = Array.Empty<SinkSlot>();

    private List<EventEntry>? _bootBuffer = new(256);
    private int _bootDropped;

    // Set when a nested dispatch changed a sink's incident state and could not
    // report it; the next top-level dispatch does.
    private int _reportsPending;

    // Provider/event pairs whose Message template already failed once.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _brokenTemplates = new();

    [ThreadStatic]
    private static int _dispatchDepth;

    private static readonly IReadOnlyDictionary<string, object?> EmptyPayload =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    // EventListener's base constructor invokes OnEventSourceCreated for every
    // already-existing provider; that callback can fire before this derived
    // constructor's body runs. Those early sources are stashed and enabled once
    // the instance is ready.
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    public DispatchEventListener()
    {
        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    // No dedupe: registering a sink twice is the caller's bug. With replayBoot
    // the sink first receives the boot history still held in the ring (nothing
    // once CompleteBoot ran); the live window keeps its own longer buffer for
    // the history after boot.
    public void AddSink(ILogSink sink, bool replayBoot = false)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));
        var slot = new SinkSlot(sink);
        bool reportDue = false;
        SinkSlot[] snapshot;
        lock (_sinksLock)
        {
            if (replayBoot && _bootBuffer is not null)
            {
                foreach (EventEntry entry in _bootBuffer)
                    reportDue |= Offer(slot, entry);
            }
            _slots.Add(slot);
            snapshot = _slots.ToArray();
            Volatile.Write(ref _snapshot, snapshot);
        }

        // A sink that threw during replay opened an incident. The report is
        // emitted here, after the lock: it dispatches back through this
        // listener, which takes the same lock. Left for the next live event it
        // would stay unreported for as long as the sink is the only one failing.
        if (!reportDue) return;
        if (_dispatchDepth > 0) Volatile.Write(ref _reportsPending, 1);
        else ReportIncidents(snapshot);
    }

    public void RemoveSink(ILogSink sink)
    {
        lock (_sinksLock)
        {
            int index = _slots.FindIndex(s => ReferenceEquals(s.Sink, sink));
            if (index < 0) return;
            _slots.RemoveAt(index);
            Volatile.Write(ref _snapshot, _slots.ToArray());
        }
    }

    // Ends boot replay: the ring is released and later sinks start from their
    // registration. Called once every phase-registered sink is in place.
    public void CompleteBoot()
    {
        int dropped;
        lock (_sinksLock)
        {
            if (_bootBuffer is null) return;
            _bootBuffer = null;
            dropped = _bootDropped;
        }
        if (dropped > 0)
            DeckleDiagnosticsSource.Log.BootReplayOverflow(dropped, BootBufferCapacity);
    }

    // Providers are discovered by the `Deckle-` name prefix, never listed, so
    // no inventory can drift; a provider named otherwise is never heard.
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle-", StringComparison.Ordinal)) return;

        lock (_earlySources)
        {
            if (!_ready)
            {
                _earlySources.Add(eventSource);
                return;
            }
        }
        EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventSource.Name is null) return;

        // No sink and no boot ring means no observable consumer: avoid
        // materialising the payload dictionary and the formatted message.
        SinkSlot[] snapshot = Volatile.Read(ref _snapshot);
        bool booting = Volatile.Read(ref _bootBuffer) is not null;
        if (snapshot.Length == 0 && !booting) return;

        var entry = BuildEntry(eventData, out Exception? templateError);

        if (booting)
        {
            lock (_sinksLock)
            {
                if (_bootBuffer is not null)
                {
                    if (_bootBuffer.Count < BootBufferCapacity) _bootBuffer.Add(entry);
                    else _bootDropped++;
                }
                // Read under the lock: a sink replaying the ring right now
                // either saw this entry in the ring or is in this snapshot.
                snapshot = _snapshot;
            }
        }

        Dispatch(entry, snapshot);

        // After the row, so the report reads as its consequence in the journal.
        if (templateError is not null) ReportBrokenTemplate(entry, templateError);
    }

    // A template that cannot be formatted is a defect of the provider, not of
    // one emission: the same event would fail on every call. Naming the pair
    // once is enough, and the dedupe is also what bounds the recursion, since
    // this report dispatches back through OnEventWritten.
    private void ReportBrokenTemplate(EventEntry entry, Exception error)
    {
        if (!_brokenTemplates.TryAdd($"{entry.Provider}/{entry.EventName}", 0)) return;

        DeckleDiagnosticsSource.Log.MessageTemplateBroken(
            entry.Provider, entry.EventName, error.GetType().Name);
    }

    private void Dispatch(EventEntry entry, SinkSlot[] snapshot)
    {
        _dispatchDepth++;
        try
        {
            bool stateChanged = false;
            foreach (SinkSlot slot in snapshot)
                stateChanged |= Offer(slot, entry);

            if (_dispatchDepth > 1)
            {
                if (stateChanged) Volatile.Write(ref _reportsPending, 1);
                return;
            }

            if (stateChanged || Volatile.Read(ref _reportsPending) != 0)
                ReportIncidents(snapshot);
        }
        finally
        {
            _dispatchDepth--;
        }
    }

    // Delivers one entry to one sink and keeps the sink's incident state.
    // Returns true when that state changed (an incident opened or closed), so
    // the caller knows a report is due; the report itself is emitted later,
    // outside any per-slot lock.
    //
    // Only a Write that returned closes an incident. An entry the sink does not
    // want proves nothing about its health, and the fault report itself is such
    // an entry: it dispatches back through here, and counting it as a success
    // would close the incident the same instant it opened.
    private static bool Offer(SinkSlot slot, EventEntry entry)
    {
        try
        {
            if (!slot.Sink.Wants(entry)) return false;
            slot.Sink.Write(entry);
        }
        catch (Exception ex)
        {
            lock (slot)
            {
                slot.Failures++;
                if (slot.Faulted) return false;
                slot.Faulted = true;
                slot.FaultError = ex;
                slot.FaultReported = false;
                return true;
            }
        }

        // The happy path reads one volatile flag; the lock is taken only to
        // close an incident.
        if (!Volatile.Read(ref slot.Faulted)) return false;
        lock (slot)
        {
            if (!slot.Faulted) return false;
            slot.Faulted = false;
            slot.RecoveredAfter = slot.Failures;
            slot.Failures = 0;
            slot.RecoveryPending = true;
            return true;
        }
    }

    private void ReportIncidents(SinkSlot[] snapshot)
    {
        Volatile.Write(ref _reportsPending, 0);
        foreach (SinkSlot slot in snapshot)
        {
            string? faultType = null, faultMessage = null;
            int recoveredAfter = -1;
            lock (slot)
            {
                if (slot.Faulted && !slot.FaultReported)
                {
                    slot.FaultReported = true;
                    faultType = slot.FaultError?.GetType().Name ?? "(unknown)";
                    faultMessage = slot.FaultError?.Message ?? string.Empty;
                }
                if (slot.RecoveryPending)
                {
                    slot.RecoveryPending = false;
                    recoveredAfter = slot.RecoveredAfter;
                }
            }
            if (faultType is not null)
                DeckleDiagnosticsSource.Log.SinkFaulted(slot.Name, faultType, faultMessage!);
            if (recoveredAfter >= 0)
                DeckleDiagnosticsSource.Log.SinkRecovered(slot.Name, recoveredAfter);
        }
    }

    // The payload keys are the [Event] parameter names (snake_case by Deckle
    // convention); the formatted message is null when the provider declared no
    // Message template.
    private static EventEntry BuildEntry(EventWrittenEventArgs e, out Exception? templateError)
    {
        templateError = null;
        var names = e.PayloadNames;
        var values = e.Payload;
        int count = names is null ? 0 : names.Count;
        IReadOnlyDictionary<string, object?> payload = EmptyPayload;
        if (count > 0)
        {
            var dict = new Dictionary<string, object?>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string key = names![i];
                object? value = (values is not null && i < values.Count) ? values[i] : null;
                dict[key] = value;
            }
            payload = dict;
        }

        string? formatted = null;
        if (!string.IsNullOrEmpty(e.Message) && values is not null)
        {
            try
            {
                var arr = new object?[values.Count];
                for (int i = 0; i < values.Count; i++) arr[i] = values[i];
                formatted = string.Format(System.Globalization.CultureInfo.InvariantCulture, e.Message, arr);
            }
            catch (Exception ex)
            {
                // A malformed template must not break the pipeline; leave
                // formatted null and let sinks render the raw payload. The
                // caller reports it, once per event.
                templateError = ex;
            }
        }

        return new EventEntry(
            timestamp: DateTimeOffset.Now,
            provider: e.EventSource.Name!,
            eventName: e.EventName ?? "(unnamed)",
            level: e.Level,
            keywords: e.Keywords,
            kind: ObservationTags.GetKind(e.Tags),
            formattedMessage: formatted,
            payload: payload,
            journal: ObservationTags.IsJournal(e.Tags));
    }

    // Clean-shutdown barrier for sinks that hand work to a background writer.
    // Call only after runtime producers have stopped and after the final event
    // worth retaining. Each sink drains entries accepted before this call, or
    // gives up at the timeout: a crash handler cannot wait on a stuck disk.
    // Every failure is named in the result and emitted on Deckle-Diagnostics,
    // which reaches the sinks still able to write.
    public IReadOnlyList<SinkFlushFailure> FlushSinks() => FlushSinks(Timeout.InfiniteTimeSpan);

    public IReadOnlyList<SinkFlushFailure> FlushSinks(TimeSpan timeout)
    {
        var failures = new List<SinkFlushFailure>();
        SinkSlot[] snapshot = Volatile.Read(ref _snapshot);
        foreach (SinkSlot slot in snapshot)
        {
            if (slot.Sink is not IFlushableLogSink flushable) continue;
            try
            {
                if (!flushable.TryFlush(timeout))
                    failures.Add(new SinkFlushFailure(slot.Name, null));
            }
            catch (Exception ex)
            {
                failures.Add(new SinkFlushFailure(slot.Name, ex));
            }
        }

        foreach (SinkFlushFailure failure in failures)
        {
            if (failure.Error is null)
                DeckleDiagnosticsSource.Log.FlushTimedOut(failure.Sink, (int)timeout.TotalMilliseconds);
            else
                DeckleDiagnosticsSource.Log.FlushFailed(
                    failure.Sink, failure.Error.GetType().Name, failure.Error.Message);
        }
        return failures;
    }

    private sealed class SinkSlot
    {
        public SinkSlot(ILogSink sink)
        {
            Sink = sink;
            Name = sink.GetType().Name;
        }

        public ILogSink Sink { get; }
        public string Name { get; }

        // Guarded by `lock (this)`, except Faulted which is also read volatile
        // on the happy path.
        public bool Faulted;
        public bool FaultReported;
        public Exception? FaultError;
        public int Failures;
        public bool RecoveryPending;
        public int RecoveredAfter;
    }
}

// One sink that could not drain at FlushSinks: Error is null when it ran out
// of time, the exception otherwise.
public sealed record SinkFlushFailure(string Sink, Exception? Error);
