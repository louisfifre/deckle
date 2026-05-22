using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics.Listeners;

// Listens to every Deckle.* EventSource and forwards each event to an
// ILogWindowSink. The sink is supplied once at construction; the
// listener takes care of selecting providers (any whose Name starts
// with "Deckle.") and of materialising the payload dictionary so the
// sink doesn't have to know about EventListener internals.
//
// Lifetime. Constructed once at App boot, kept alive for the life of
// the process. EventListener auto-discovers EventSources created
// after the listener (the OnEventSourceCreated callback fires every
// time a new provider is instantiated), so providers declared in
// modules loaded lazily still light up.
//
// Threading. EventListener.OnEventWritten fires on the emitting
// thread — same posture as the legacy TelemetryService sinks. The
// ILogWindowSink implementation is responsible for marshalling to
// the UI thread if it needs to (e.g. via DispatcherQueue).
public sealed class LogWindowEventListener : EventListener
{
    private readonly ILogWindowSink _sink;

    // We collect EventSources observed before _sink was assigned, then
    // enable them in the constructor body. EventListener's base
    // constructor invokes OnEventSourceCreated for every already-
    // existing provider; that callback can fire before the derived
    // constructor's field initialisers run, so _sink may not be set
    // yet on the first calls. Buffer + re-enable pattern from
    // https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlistener.
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    public LogWindowEventListener(ILogWindowSink sink)
    {
        _sink = sink;
        // Now that _sink is wired, light up everything we saw during
        // base-class init.
        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle.", System.StringComparison.Ordinal)) return;

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
        var entry = BuildEntry(eventData);
        try { _sink.Write(entry); }
        catch { /* A sink must never crash the emitter. */ }
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
