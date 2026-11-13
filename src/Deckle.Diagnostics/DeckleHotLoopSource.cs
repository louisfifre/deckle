using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider of the hot loops: the work that ticks many times a
// second or minute (a heartbeat, a probe). A loop like that cannot write a
// line per tick into the always-on record, so it counts its ticks (LoopPulse)
// and this provider speaks only when the count stops. The per-tick detail
// events stay where they are, Verbose and off by default.
//
// Two lines tell the story of a loop. LoopStalled opens one Warning incident
// when a loop that should be running ticked zero times in a window, and
// LoopRecovered closes it, once, when the count moves again or the loop is no
// longer expected to run. A loop that works writes nothing: it needs no line in
// the one record a reader on another machine gets.
//
// The `loop` vocabulary is the pulses of HotLoops (LoopPulse.cs): a call site
// ticks a pulse, it never passes a literal.
[EventSource(Name = "Deckle-HotLoop")]
public sealed class DeckleHotLoopSource : DeckleEventSource
{
    public static readonly DeckleHotLoopSource Log = new();

    private DeckleHotLoopSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    // 1 retired (LoopRollup, one row per loop per window).
    // 4 retired (LoopCounts, every loop's ticks on one row per window; nobody
    // read it, 2026-11-13).
    public const int EvtLoopStalled   = 2;
    public const int EvtLoopRecovered = 3;

    // The loop was expected to run in two windows in a row and ticked zero times
    // in the second. `total` is its tick count since the process started, so a
    // reader sees whether it ever worked.
    [Event(EvtLoopStalled,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Tags = ObservationTags.Milestone,
           Message = "A loop that should be running stopped ticking\nloop={0} | window_s={1} | total={2}")]
    public void LoopStalled(string loop, int window_s, long total)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLoopStalled, loop, window_s, total);
    }

    // reason is `resumed` (the count moved again) or `stopped` (the loop is no
    // longer expected to run, so the silence is no longer a fault).
    [Event(EvtLoopRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Tags = ObservationTags.Milestone,
           Message = "A stalled loop is settled")]
    public void LoopRecovered(string loop, string reason, int stalled_s)
    {
        if (!IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLoopRecovered, loop, reason, stalled_s);
    }
}
