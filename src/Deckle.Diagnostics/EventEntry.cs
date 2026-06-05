using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Lightweight DTO carrying everything a sink needs to render an event.
// Built once per emission by the listener layer from EventWrittenEventArgs
// and handed to every registered sink. POCO with positional values — no
// allocations beyond the entry itself once the payload dictionary is
// materialised by the listener.
//
// Naming. `Provider` is the EventSource Name (e.g. "Deckle-Chrono"),
// `EventName` is the method name on the provider (e.g. "ChronoStarted").
// `FormattedMessage` is what String.Format(Event.Message, payload…)
// produces when the provider declared a Message template; null when no
// template was provided. Sinks that need to render to a human surface
// (LogWindow, HUD) prefer the formatted message and fall back to a
// generic "Provider.EventName" line when null.
public sealed class EventEntry
{
    public System.DateTimeOffset Timestamp { get; }
    public string Provider { get; }
    public string EventName { get; }
    public EventLevel Level { get; }
    public EventKeywords Keywords { get; }
    public string? FormattedMessage { get; }
    public IReadOnlyDictionary<string, object?> Payload { get; }

    public EventEntry(
        System.DateTimeOffset timestamp,
        string provider,
        string eventName,
        EventLevel level,
        EventKeywords keywords,
        string? formattedMessage,
        IReadOnlyDictionary<string, object?> payload)
    {
        Timestamp = timestamp;
        Provider = provider;
        EventName = eventName;
        Level = level;
        Keywords = keywords;
        FormattedMessage = formattedMessage;
        Payload = payload;
    }
}
