using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Declares which observability stream owns an EventSource event. Operational
// observations feed human journals; Dataset observations feed only their
// purpose-specific, consented telemetry route. The two are intentionally
// exclusive: a fact needed in both places is emitted twice with two contracts.
public enum ObservationKind
{
    Operational,
    Dataset,
}

// EventSource tags are user-defined metadata carried through
// EventWrittenEventArgs. Keeping the marker here gives every provider one
// dependency-neutral way to declare a dataset event without spending a shared
// keyword bit or relying on an event-name blacklist in consumers.
public static class ObservationTags
{
    public const EventTags Dataset = (EventTags)0x1;

    public static ObservationKind GetKind(EventTags tags)
        => (tags & Dataset) != 0
            ? ObservationKind.Dataset
            : ObservationKind.Operational;
}
