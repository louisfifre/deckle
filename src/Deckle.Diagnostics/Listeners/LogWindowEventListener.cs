using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics.Listeners;

// Listens to every Deckle.* EventSource and forwards each event to the
// registered ILogWindowSink(s). The listener is constructed once at App
// boot and starts buffering immediately; sinks are attached lazily as
// the LogWindow surface comes online (typically on first user open).
//
// Lifetime. Constructed once at App boot, kept alive for the life of
// the process. EventListener auto-discovers EventSources created
// after the listener (the OnEventSourceCreated callback fires every
// time a new provider is instantiated), so providers declared in
// modules loaded lazily still light up.
//
// Threading. EventListener.OnEventWritten fires on the emitting
// thread. The ILogWindowSink implementation is responsible for
// marshalling to the UI thread if it needs to (e.g. via DispatcherQueue).
//
// Buffer for lazy LogWindow. The LogWindow is created lazily on first user
// open; events emitted during boot must be visible as soon as it opens. The
// listener keeps a fixed-capacity ring (5000) and replays it in full when a
// sink attaches through `AttachSink`. Replaces the old legacy
// `TelemetryService._history` with the same history guarantee.
public sealed class LogWindowEventListener : EventListener
{
    private const int BufferCapacity = 5000;

    private readonly List<ILogWindowSink> _sinks = new();
    private readonly List<EventEntry> _buffer = new(capacity: BufferCapacity);
    private readonly object _lock = new();

    // Optional drop filter. When non-null and returns true, the entry is
    // ignored BEFORE insertion into the ring buffer and BEFORE broadcast to
    // sinks. Direct consequence: a filtered entry will not be replayed by
    // AttachSink either, since it never landed in the buffer. Deliberate
    // posture: the filter expresses "this event is not meant to exist in the
    // live log window", not temporary display masking.
    //
    // Wired by the host through ConfigureDropFilter. Currently UNWIRED: App
    // routes ambient/streaming Verbose silencing through the provider-level
    // filter (_providerLevelDropFilter) instead, so this entry-level hook has
    // no caller today. Kept as the symmetric per-entry counterpart for a host
    // that needs to drop by built EventEntry.
    private Func<EventEntry, bool>? _dropFilter;
    private Func<string, EventLevel, EventKeywords, bool>? _providerLevelDropFilter;

    // We collect EventSources observed before the derived constructor
    // is ready, then enable them in the constructor body. EventListener's
    // base constructor invokes OnEventSourceCreated for every already-
    // existing provider; that callback can fire before the derived
    // constructor's field initialisers run, so the listener may not be
    // ready yet on the first calls.
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    public LogWindowEventListener()
    {
        // Now that fields are wired, light up everything we saw during
        // base-class init.
        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    // Attaches a sink and replays the buffered history since boot. Replay is
    // done under the buffer lock so no event can slip between snapshot copy and
    // sink registration; an event arriving during replay will be captured in
    // the live path, never lost or duplicated.
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
            catch { /* A sink must never crash the listener. */ }
        }
    }

    public void DetachSink(ILogWindowSink sink)
    {
        lock (_lock) _sinks.Remove(sink);
    }

    // Installs a single drop filter. Only one filter is active at a time; a new
    // call replaces the previous one. Null uninstalls. The filter is consulted
    // in OnEventWritten before insertion into the buffer and before broadcast
    // to sinks; a filtered entry is therefore never seen by sinks (neither live
    // nor during AttachSink replay).
    public void ConfigureDropFilter(Func<EventEntry, bool> filter)
    {
        _dropFilter = filter;
    }

    // Early drop filter, consulted before BuildEntry. Use for noisy families
    // where provider + level + keywords are enough to decide, to avoid payload
    // / format string allocations. Keywords are provided so a periodic rollup
    // (Heartbeat keyword) can be exempted from a drop targeting per-tick
    // Verbose; ETW already exposes keywords on EventWrittenEventArgs before any
    // BuildEntry, so the exemption remains allocation-free.
    public void ConfigureProviderLevelDropFilter(Func<string, EventLevel, EventKeywords, bool> filter)
    {
        _providerLevelDropFilter = filter;
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle-", System.StringComparison.Ordinal)) return;

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
        string? provider = eventData.EventSource.Name;
        if (provider is null) return;

        var providerLevelDropFilter = _providerLevelDropFilter;
        if (providerLevelDropFilter is not null)
        {
            try { if (providerLevelDropFilter(provider, eventData.Level, eventData.Keywords)) return; }
            catch { /* A filter must never crash the listener. */ }
        }

        var entry = BuildEntry(eventData);

        // Drop filter consulted before the buffer so a filtered entry is
        // neither replayed nor broadcast. Unlocked field read: a race during
        // ConfigureDropFilter may at worst pass one event too many or too few,
        // never corruption.
        var dropFilter = _dropFilter;
        if (dropFilter is not null)
        {
            try { if (dropFilter(entry)) return; }
            catch { /* A filter must never crash the listener. */ }
        }

        ILogWindowSink[] snapshot;
        lock (_lock)
        {
            // Ring: bound the buffer so it does not grow indefinitely on long
            // sessions. When the cap is exceeded, discard the oldest entry:
            // same posture as `LogWindow` on the UI side (cap 5000 in
            // `_entries`). Capacity matches so opening replay fills exactly
            // the window the user will see.
            _buffer.Add(entry);
            if (_buffer.Count > BufferCapacity) _buffer.RemoveAt(0);
            snapshot = _sinks.ToArray();
        }

        foreach (var sink in snapshot)
        {
            try { sink.Write(entry); }
            catch { /* A sink must never crash the emitter. */ }
        }
    }

    internal static EventEntry BuildEntry(EventWrittenEventArgs e)
    {
        var dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        var names = e.PayloadNames;
        var values = e.Payload;
        int count = names is null ? 0 : names.Count;
        for (int i = 0; i < count; i++)
        {
            string key = names![i];
            object? value = (values is not null && i < values.Count) ? values[i] : null;
            dict[key] = value;
        }

        // EventWrittenEventArgs.Message is the template declared via
        // the [Event(Message = "…")] attribute. String.Format with
        // the payload yields the human-readable line. Null when the
        // provider didn't supply a template; sinks fall back to a
        // generic "Provider.EventName" rendering.
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
                // A malformed template should not break the pipeline.
                // Leave formatted = null and let the sink render the
                // raw payload instead.
            }
        }

        return new EventEntry(
            timestamp: System.DateTimeOffset.Now,
            provider: e.EventSource.Name!,
            eventName: e.EventName ?? "(unnamed)",
            level: e.Level,
            keywords: e.Keywords,
            formattedMessage: formatted,
            payload: dict);
    }
}
