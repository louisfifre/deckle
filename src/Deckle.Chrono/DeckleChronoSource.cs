using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Chrono;

// Chronometer provider. The two pilot events carry no chronometer data and
// stay as the test target for the EventSource chain; ClockStopped is the one
// observation of the timer itself.
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
    public const int EvtClockStopped       = 3;

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

    // The time the HUD clock showed when it stopped, once per take. Compared
    // with the Audio provider's recording duration it shows a clock that
    // drifted from the capture. Detail of the dictation workflow, so it obeys
    // the transcription scope like the capture details.
    [Event(EvtClockStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "clock stopped | elapsed_ms={0}")]
    public void ClockStopped(long elapsed_ms)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtClockStopped, elapsed_ms);
    }
}
