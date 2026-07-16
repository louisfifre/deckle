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
// Lifetime. Constructed once at App boot and kept alive for the whole process;
// the EventListener registration is dropped implicitly at process exit. Sinks
// are added as the boot sequence brings them online (the always-on local sinks
// first, the opt-in telemetry sinks and the live-window sink later) — the
// subscription exists from the first AddSink-bearing boot step, so coverage
// starts as early as the earliest sink.
//
// Threading. OnEventWritten fires on the emitting thread. Sink mutations build
// and publish an immutable array under a short lock; event dispatch only reads
// that snapshot, with no lock or per-event array allocation. A sink marshals to
// its own thread if it needs to. A sink that throws is contained — its failure
// never reaches the emitter.
public sealed class DispatchEventListener : EventListener
{
    private readonly List<ILogSink> _sinks = new();
    private readonly object _sinksLock = new();
    private ILogSink[] _sinkSnapshot = Array.Empty<ILogSink>();

    private static readonly IReadOnlyDictionary<string, object?> EmptyPayload =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    // EventListener's base constructor invokes OnEventSourceCreated for every
    // already-existing provider; that callback can fire before this derived
    // constructor's body runs. We stash those early sources and enable them once
    // the instance is ready — same guard the former per-sink listeners used.
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

    // Registers a passive sink. Idempotent registration is the caller's
    // concern; the dispatcher appends and snapshots, it does not dedupe. Added
    // sinks receive events emitted AFTER registration — there is no retroactive
    // replay here, by design. A sink that needs boot history (the live window)
    // owns its own buffer and replays on attach.
    public void AddSink(ILogSink sink)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));
        lock (_sinksLock)
        {
            _sinks.Add(sink);
            Volatile.Write(ref _sinkSnapshot, _sinks.ToArray());
        }
    }

    public void RemoveSink(ILogSink sink)
    {
        lock (_sinksLock)
        {
            if (_sinks.Remove(sink))
                Volatile.Write(ref _sinkSnapshot, _sinks.ToArray());
        }
    }

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

        // No sink means no observable consumer: avoid materialising the
        // payload dictionary and formatted message while the listener is
        // subscribed early during boot.
        ILogSink[] snapshot = Volatile.Read(ref _sinkSnapshot);
        if (snapshot.Length == 0) return;

        var entry = BuildEntry(eventData);
        foreach (var sink in snapshot)
        {
            try { if (sink.Wants(entry)) sink.Write(entry); }
            catch { /* A sink must never crash the dispatcher. */ }
        }
    }

    // Builds the single EventEntry shared by every sink. The dictionary keys are
    // the [Event] parameter names (snake_case by Deckle convention); the
    // formatted message is String.Format of the [Event(Message=…)] template with
    // the payload, null when the provider declared no template.
    private static EventEntry BuildEntry(EventWrittenEventArgs e)
    {
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
            catch
            {
                // A malformed template must not break the pipeline; leave
                // formatted null and let sinks render the raw payload.
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
            payload: payload);
    }

    // Clean-shutdown barrier for sinks that hand work to a background writer.
    // Call only after runtime producers have stopped and after the final event
    // worth retaining. Each sink drains entries accepted before this snapshot;
    // ordinary passive sinks require no action. False reports that at least one
    // destination could not persist its accepted tail.
    public bool FlushSinks()
    {
        bool succeeded = true;
        ILogSink[] snapshot = Volatile.Read(ref _sinkSnapshot);
        foreach (ILogSink sink in snapshot)
        {
            if (sink is not IFlushableLogSink flushable) continue;
            try { flushable.Flush(); }
            catch { succeeded = false; }
        }
        return succeeded;
    }
}
