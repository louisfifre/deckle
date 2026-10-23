using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Built once per emission by DispatchEventListener and shared by every sink.
//
// `Provider` is the EventSource Name (e.g. "Deckle-Chrono"), `EventName` the
// method name on the provider. `FormattedMessage` is null when the provider
// declared no Message template; LogLineFormatter then falls back to the
// event name. `Milestone` is the ObservationTags.Milestone mark: a milestone the
// always-on log file keeps whatever its level.
public sealed class EventEntry
{
    public System.DateTimeOffset Timestamp { get; }
    public string Provider { get; }
    public string EventName { get; }
    public EventLevel Level { get; }
    public EventKeywords Keywords { get; }
    public ObservationKind Kind { get; }
    public bool Milestone { get; }
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
        bool milestone = false)
    {
        Timestamp = timestamp;
        Provider = provider;
        EventName = eventName;
        Level = level;
        Keywords = keywords;
        Kind = kind;
        Milestone = milestone;
        FormattedMessage = formattedMessage;
        Payload = payload;
    }
}
