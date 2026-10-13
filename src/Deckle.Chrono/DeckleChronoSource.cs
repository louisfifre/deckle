using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Chrono;

// Pilot provider: its events carry no chronometer data and serve as the test
// target for the EventSource chain.
[EventSource(Name = "Deckle-Chrono")]
public sealed class DeckleChronoSource : DeckleEventSource
{
    public static readonly DeckleChronoSource Log = new();

    private DeckleChronoSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest: never reuse one after deleting an
    // event, a consumer may still need it to decode a dump.
    public const int EvtPilotEmitted       = 1;
    public const int EvtPilotEmittedDetail = 2;

    [Event(EvtPilotEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Chrono pilot emitted")]
    public void PilotEmitted()
    {
        if (IsEnabled()) WriteEvent(EvtPilotEmitted);
    }

    [Event(EvtPilotEmittedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pilot emitted | note={0}")]
    public void PilotEmittedDetail(string note)
    {
        if (IsEnabled()) WriteEvent(EvtPilotEmittedDetail, note);
    }
}
