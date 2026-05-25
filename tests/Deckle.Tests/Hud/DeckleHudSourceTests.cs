using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Hud;
using Deckle.Tests.Shared;
using Xunit;

namespace Deckle.Tests.Hud;

// Couverture observability du provider Deckle.Hud — restreinte ici à
// ProximityRollup parce que c'est l'event qui vient de basculer de
// signature (period_ms → duration_ms) et de sémantique (périodique 1 s
// → per-session). Les autres axes (StateChanged, FadeInStarted, etc.)
// ne sont pas couverts dans cette passe ; ils s'ajouteront au fil des
// chantiers qui les touchent.
[Trait("Category", "observability")]
public class DeckleHudSourceTests
{
    [Fact]
    public void ProximityRollupEmitsVerboseOnHeartbeatKeyword()
    {
        using var listener = new TestEventListener("Deckle.Hud");

        DeckleHudSource.Log.ProximityRollup(
            duration_ms: 4200, samples: 525,
            min_alpha: 100, max_alpha: 240,
            p50_cursor_dist_dip: 64, p95_cursor_dist_dip: 12);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleHudSource.EvtProximityRollup, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Heartbeat));
        Assert.Equal(4200, ev.Payload?[0]);
        Assert.Equal(525, ev.Payload?[1]);
        Assert.Equal((byte)100, ev.Payload?[2]);
        Assert.Equal((byte)240, ev.Payload?[3]);
        Assert.Equal(64, ev.Payload?[4]);
        Assert.Equal(12, ev.Payload?[5]);
    }
}
