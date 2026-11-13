using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider of the hot loops: the work that ticks many times a
// second or minute (a heartbeat, a request, a probe, a marshalled callback). A
// loop like that cannot write a line per tick into the always-on record, so it
// counts (LoopPulse) and this provider carries the count once a minute. The
// per-tick detail events stay where they are, Verbose and off by default.
//
// Three lines tell the whole story of the loops. LoopCounts says how many
// ticks each loop held in a window, all loops on one line. LoopStalled opens one Warning incident when a loop that should
// be running ticked zero times in a window, and LoopRecovered closes it, once,
// when the count moves again or the loop is no longer expected to run. Both
// travel in the log; the rollup does not, because a loop that works needs
// no line in the one record a reader on another machine gets. The rollup is
// detail, so it is Verbose: at Informational it reached the LogWindow with
// every run-story milestone and, once a minute per live loop, read as noise
// a person could not filter away without losing the milestones too.
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
    public const int EvtLoopStalled   = 2;
    public const int EvtLoopRecovered = 3;
    public const int EvtLoopCounts    = 4;

    // One row per window for every loop that ticked: `window_s` first, the
    // field every row shares, then `counts`, `loop:ticks` pairs separated by a
    // space in registration order. It replaced one row per loop, six at the
    // same millisecond each minute, that read as noise and told the reader
    // nothing apart. A window where no loop ticked writes nothing.
    [Event(EvtLoopCounts,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "window_s={0} | counts={1}")]
    public void LoopCounts(int window_s, string counts)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLoopCounts, window_s, counts);
    }

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