using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Declares which observability stream owns an EventSource event. Operational
// observations feed the operational log stream; Dataset observations feed only their
// purpose-specific, consented telemetry route. The two are intentionally
// exclusive: a fact needed in both places is emitted twice with two contracts.
public enum ObservationKind
{
    Operational,
    Dataset,
}

// EventSource tags are user-defined metadata carried through
// EventWrittenEventArgs. Keeping the markers here gives every provider one
// dependency-neutral way to declare them without spending a shared keyword
// bit or relying on an event-name blacklist in consumers.
public static class ObservationTags
{
    public const EventTags Dataset = (EventTags)0x1;

    // The run journal: the few Informational milestones that tell a run's
    // story (starting, ready, module up or skipped, shutdown, exit) and that
    // the always-on journal file keeps beside every Warning and Error. A
    // reader on another machine must find them without any verbosity control
    // turned on, so a provider tags them rather than hoping a level predicate
    // catches them.
    public const EventTags Journal = (EventTags)0x2;

    public static ObservationKind GetKind(EventTags tags)
        => (tags & Dataset) != 0
            ? ObservationKind.Dataset
            : ObservationKind.Operational;

    public static bool IsJournal(EventTags tags) => (tags & Journal) != 0;
}
