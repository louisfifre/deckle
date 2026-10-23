using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider of the hot loops: the work that ticks many times a
// second or minute (a heartbeat, a request, a probe, a marshalled callback). A
// loop like that cannot write a line per tick into the always-on record, so it
// counts (LoopPulse) and this provider carries the count once a minute. The
// per-tick detail events stay where they are, Verbose and off by default.
//
// Three lines tell the whole story of a loop. LoopRollup says how many ticks a
// window held. LoopStalled opens one Warning incident when a loop that should
// be running ticked zero times in a window, and LoopRecovered closes it, once,
// when the count moves again or the loop is no longer expected to run. Both
// travel in the log; the rollup does not, because a loop that works needs
// no line in the one record a reader on another machine gets.
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
    public const int EvtLoopRollup    = 1;
    public const int EvtLoopStalled   = 2;
    public const int EvtLoopRecovered = 3;

    // `count` is the ticks of the window, `window_s` the seconds it spans. A
    // window with no tick writes nothing: a quiet loop is not an event.
    [Event(EvtLoopRollup,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "loop rollup | loop={0} | count={1} | window_s={2}")]
    public void LoopRollup(string loop, long count, int window_s)
    {
        if (!IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLoopRollup, loop, count, window_s);
    }

    // The loop was expected to run in two windows in a row and ticked zero times
    // in the second. `total` is its tick count since the process started, so a
    // reader sees whether it ever worked.
    [Event(EvtLoopStalled,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Tags = ObservationTags.Milestone,
           Message = "A loop that should be running stopped ticking | loop={0} | window_s={1} | total={2}")]
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
           Message = "A stalled loop is settled | loop={0} | reason={1} | stalled_s={2}")]
    public void LoopRecovered(string loop, string reason, int stalled_s)
    {
        if (!IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLoopRecovered, loop, reason, stalled_s);
    }
}