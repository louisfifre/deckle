using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Chrono;

// Pilot EventSource to exercise all upstream plumbing: shared SessionId,
// EventListener dynamically discovering the provider, JSONL serialization,
// LogWindow feeding.
//
// Events here carry simple milestone semantics: no structured payload, no
// aggregated heartbeat. When an application call site actually needs to
// instrument the chronometer (for example when the surface wave redesigns the
// HUD), this will be extended with additional [Event] entries while respecting
// the brief's doctrine: one [Event] per distinct operation.
[EventSource(Name = "Deckle-Chrono")]
public sealed class DeckleChronoSource : DeckleEventSource
{
    public static readonly DeckleChronoSource Log = new();

    private DeckleChronoSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // Sequential numbering from 1. IDs are public in the ETW manifest; do not
    // reuse an ID after deleting an event (a historical consumer may still need
    // it to decode a dump). For Deckle we start fresh at wave 1, so there is no
    // history to preserve.
    public const int EvtPilotEmitted = 1;

    // Boot validation event. Emitted once at App startup to exercise
    // EventSource → JsonlEventListener → app.jsonl and EventSource →
    // LogWindowEventListener → LogWindow.
    [Event(EvtPilotEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Chrono pilot emission ({0})")]
    public void PilotEmitted(string note)
    {
        if (IsEnabled()) WriteEvent(EvtPilotEmitted, note);
    }
}
