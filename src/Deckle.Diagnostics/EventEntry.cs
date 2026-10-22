using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Built once per emission by DispatchEventListener and shared by every sink.
//
// `Provider` is the EventSource Name (e.g. "Deckle-Chrono"), `EventName` the
// method name on the provider. `FormattedMessage` is null when the provider
// declared no Message template; LogLineFormatter then falls back to the
// event name. `Journal` is the ObservationTags.Journal mark: a milestone the
// always-on journal file keeps whatever its level.
public sealed class EventEntry
{
    public System.DateTimeOffset Timestamp { get; }
    public string Provider { get; }
    public string EventName { get; }
    public EventLevel Level { get; }
    public EventKeywords Keywords { get; }
    public ObservationKind Kind { get; }
    public bool Journal { get; }
    public string? FormattedMessage { get; }
    public IReadOnlyDictionary<string, object?> Payload { get; }

    public EventEntry(
        System.DateTimeOffset timestamp,
        string provider,
        string eventName,
        EventLevel level,
        EventKeywords keywords,
        ObservationKind kind,
        string? formattedMessage,
        IReadOnlyDictionary<string, object?> payload,
        bool journal = false)
    {
        Timestamp = timestamp;
        Provider = provider;
        EventName = eventName;
        Level = level;
        Keywords = keywords;
        Kind = kind;
        Journal = journal;
        FormattedMessage = formattedMessage;
        Payload = payload;
    }
}
