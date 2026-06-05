using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Deckle.Vision;
using Xunit;

namespace Deckle.Vision.Tests;

// Vision module: capture heartbeat coverage (one representative event of the
// cross-cutting heartbeat sub-provider as consumed by ScreenCaptureService).
// The heartbeat is strictly gated by Verbose + Keywords.Heartbeat; without an
// attached listener, cost collapses to IsEnabled.
[Trait("Category", "observability")]
public class DeckleVisionSourceTests
{
    [Fact]
    public void HeartbeatEmitsVerboseOnHeartbeatKeyword()
    {
        using var listener = new TestEventListener("Deckle.Vision");

        DeckleVisionSource.Log.Heartbeat(
            period_ms: 1000, frames_acquired: 60, frames_dropped: 0,
            p50_acquire_us: 250, p95_acquire_us: 800,
            p50_sample_us: 1200, p95_sample_us: 3400);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleVisionSource.EvtHeartbeat, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Heartbeat));
    }

    [Fact]
    public void HeartbeatCarriesPercentilesInOrder()
    {
        using var listener = new TestEventListener("Deckle.Vision");

        DeckleVisionSource.Log.Heartbeat(
            period_ms: 1000, frames_acquired: 15, frames_dropped: 2,
            p50_acquire_us: 100, p95_acquire_us: 500,
            p50_sample_us: 900, p95_sample_us: 2100);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(1000, ev.Payload?[0]);
        Assert.Equal(15, ev.Payload?[1]);
        Assert.Equal(2, ev.Payload?[2]);
        Assert.Equal(100L, ev.Payload?[3]);
        Assert.Equal(500L, ev.Payload?[4]);
        Assert.Equal(900L, ev.Payload?[5]);
        Assert.Equal(2100L, ev.Payload?[6]);
    }
}
