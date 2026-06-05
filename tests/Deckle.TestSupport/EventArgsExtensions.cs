using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.TestSupport;

// ── EventArgsExtensions ─────────────────────────────────────────────────────
//
// EventSource adds high system bits (bits 44-47, reserved ETW sessions) to
// `EventWrittenEventArgs.Keywords` in addition to keywords declared on the
// event. A strict assertion
// `Assert.Equal((EventKeywords)Keywords.Network, ev.Keywords)` fails because
// the right-hand member carries Deckle bits PLUS system bits.
//
// `HasKeyword` masks on the Deckle bit to answer the real question asked by
// tests: "is this event on this Deckle keyword" without caring about ETW bits
// added by the framework.
public static class EventArgsExtensions
{
    public static bool HasKeyword(this EventWrittenEventArgs ev, Keywords k)
        => ((long)ev.Keywords & (long)k) == (long)k;
}
