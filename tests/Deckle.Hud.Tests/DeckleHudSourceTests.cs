using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Hud;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Hud.Tests;

// Observability coverage grows with the workstream touching each event family:
// rollup semantics and the Warning/Verbose split are pinned here.
[Trait("Category", "observability")]
public class DeckleHudSourceTests
{
    [Fact]
    public void ProximityRollupEmitsVerboseOnHeartbeatKeyword()
    {
        using var listener = new TestEventListener("Deckle-Hud");

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

    [Fact]
    public void RevealMaskFailureSeparatesWarningFromVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.RevealMaskFailed();
        DeckleHudSource.Log.RevealMaskFailedDetail("COMException", "The parameter is incorrect.");

        Assert.Collection(listener.Events,
            warning =>
            {
                Assert.Equal(DeckleHudSource.EvtRevealMaskFailed, warning.EventId);
                Assert.Equal(EventLevel.Warning, warning.Level);
                Assert.True(warning.HasKeyword(Keywords.Lifecycle));
                Assert.Equal(0, warning.Payload?.Count ?? 0);
            },
            detail =>
            {
                Assert.Equal(DeckleHudSource.EvtRevealMaskFailedDetail, detail.EventId);
                Assert.Equal(EventLevel.Verbose, detail.Level);
                Assert.True(detail.HasKeyword(Keywords.Lifecycle));
                Assert.Equal("COMException", detail.Payload?[0]);
                Assert.Equal("The parameter is incorrect.", detail.Payload?[1]);
            });
    }
}
