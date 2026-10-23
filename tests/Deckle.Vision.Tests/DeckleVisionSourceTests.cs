using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Deckle.Vision;
using Xunit;

namespace Deckle.Vision.Tests;

// Shares Deckle-Vision with the other class that listens to it: a listener sees every
// emission of the process-wide provider, so the two classes never run side by side.
[Collection("Vision observability")]
[Trait("Category", "observability")]
public class DeckleVisionSourceTests
{
    // The loop ends on its own (device lost, crash) before any Stop() asks: its end,
    // with the outcome and the frames it produced, must reach the journal beside the
    // session start it closes.
    [Fact]
    public void TheCaptureLoopEndTravelsInTheJournalWithItsOutcome()
    {
        using var listener = new TestEventListener("Deckle-Vision");

        DeckleVisionSource.Log.CaptureLoopEnded("failed", "device_lost", 1200, 20000);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleVisionSource.EvtCaptureLoopEnded, ev.EventId);
        Assert.True(ObservationTags.IsJournal(ev.Tags));
        Assert.Equal("failed", ev.Payload?[0]);
        Assert.Equal("device_lost", ev.Payload?[1]);
        Assert.Equal(1200L, ev.Payload?[2]);
    }

    [Fact]
    public void HeartbeatEmitsVerboseOnHeartbeatKeyword()
    {
        using var listener = new TestEventListener("Deckle-Vision");

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
        using var listener = new TestEventListener("Deckle-Vision");

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

    [Fact]
    public void FrameOwnershipAbandonmentCarriesItsCause()
    {
        using var listener = new TestEventListener("Deckle-Vision");

        // DXGI_ERROR_INVALID_CALL
        const int invalidCall = unchecked((int)0x887A0001);
        DeckleVisionSource.Log.FrameOwnershipRecoveryAbandoned(10, 10, invalidCall);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleVisionSource.EvtFrameOwnershipRecoveryAbandoned, ev.EventId);
        Assert.Equal(EventLevel.Error, ev.Level);
        Assert.Equal(10, ev.Payload?[0]);
        Assert.Equal(10, ev.Payload?[1]);
        Assert.Equal(invalidCall, ev.Payload?[2]);
    }

    [Fact]
    public void DuplicationRecreateIncidentSeparatesWarningRecoveryAndDetail()
    {
        using var listener = new TestEventListener("Deckle-Vision");

        DeckleVisionSource.Log.DuplicationRecreateAttemptFailed(
            2, unchecked((int)0x887A0026), "COMException", "msg");
        DeckleVisionSource.Log.DuplicationRecreateRecovered();
        DeckleVisionSource.Log.DuplicationRecreateEpisodeDetail(
            "recovered", failures: 2, duration_ms: 4100);

        Assert.Collection(
            listener.Events,
            incident =>
            {
                Assert.Equal(DeckleVisionSource.EvtDuplicationRecreateAttemptFailed, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
            },
            recovery =>
            {
                Assert.Equal(DeckleVisionSource.EvtDuplicationRecreateRecovered, recovery.EventId);
                Assert.Equal(EventLevel.Informational, recovery.Level);
            },
            detail =>
            {
                Assert.Equal(DeckleVisionSource.EvtDuplicationRecreateEpisodeDetail, detail.EventId);
                Assert.Equal(EventLevel.Verbose, detail.Level);
                Assert.Equal("recovered", detail.Payload?[0]);
                Assert.Equal(2, detail.Payload?[1]);
                Assert.Equal(4100L, detail.Payload?[2]);
            });
    }
}
