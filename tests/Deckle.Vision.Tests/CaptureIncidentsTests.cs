using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Vision.Tests;

// The capture loop writes one Warning when a degradation repeats and one journal
// line when it ends. A loop that exits with an incident still open has to close
// it, or the journal shows a capture that is still failing long after it stopped.
// The clock is a tick count in milliseconds, so none of this waits.
[Trait("Category", "observability")]
public sealed class CaptureIncidentsTests
{
    private const long TicksPerSecond = 1000;
    private const int AcquireThreshold = 4;

    private static CaptureIncidents NewIncidents() => new(AcquireThreshold, TicksPerSecond);

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static void Lose(CaptureIncidents incidents, int times, long startMs, long everyMs)
    {
        for (int i = 0; i < times; i++)
            incidents.AccessLost(CaptureLossCauses.AccessLost, startMs + i * everyMs);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AnAcquireIncidentOpenWhenTheLoopExitsIsClosedOnce()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        for (int i = 0; i < AcquireThreshold; i++)
            incidents.AcquireFailed(unchecked((int)0x887A0005), i * 500);

        incidents.AbandonOpen(2_000);
        incidents.AbandonOpen(2_500);

        var closer = Assert.Single(
            listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureIncidentAbandoned);
        Assert.Equal(CaptureIncidentKinds.AcquireFailures, Field(closer, "incident"));
        Assert.Equal(AcquireThreshold.ToString(), Field(closer, "count"));
        Assert.Equal("2000", Field(closer, "duration_ms"));
    }

    [Fact]
    public void AnAcquireIncidentThatRecoveredHasNothingLeftToClose()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        for (int i = 0; i < AcquireThreshold; i++)
            incidents.AcquireFailed(unchecked((int)0x887A0005), i * 500);
        incidents.AcquireReadable(2_000);
        incidents.AbandonOpen(3_000);

        Assert.Single(listener.Events, e => e.EventId == DeckleVisionSource.EvtAcquireFrameIncident);
        Assert.Single(listener.Events, e => e.EventId == DeckleVisionSource.EvtAcquireFrameRecovered);
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureIncidentAbandoned);
    }

    [Fact]
    public void ADisplayLostFewTimesIsNotAnIncident()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        Lose(incidents, CaptureIncidents.AccessLossThreshold - 1, startMs: 0, everyMs: 1_000);

        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureAccessIncident);
    }

    [Fact]
    public void LossesSpreadBeyondTheWindowNeverOpenAnIncident()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        Lose(incidents, 10, startMs: 0, everyMs: CaptureIncidents.AccessLossWindowMs + 1);

        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureAccessIncident);
    }

    [Fact]
    public void AFlappingDisplayOpensOneIncidentAndOneRecovery()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        Lose(incidents, 9, startMs: 0, everyMs: 1_000);
        incidents.Healthy(8_000 + CaptureIncidents.AccessQuietMs - 1);
        incidents.Healthy(8_000 + CaptureIncidents.AccessQuietMs);
        incidents.Healthy(8_000 + CaptureIncidents.AccessQuietMs + 500);

        var opened = Assert.Single(
            listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureAccessIncident);
        Assert.Equal(CaptureLossCauses.AccessLost, Field(opened, "cause"));
        Assert.Equal(CaptureIncidents.AccessLossThreshold.ToString(), Field(opened, "losses"));

        var recovered = Assert.Single(
            listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureAccessRecovered);
        Assert.Equal("9", Field(recovered, "losses"));
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureIncidentAbandoned);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFlappingDisplayStillOpenWhenTheLoopExitsIsClosedOnce()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        Lose(incidents, CaptureIncidents.AccessLossThreshold, startMs: 0, everyMs: 1_000);
        incidents.AbandonOpen(10_000);
        incidents.AbandonOpen(11_000);

        var closer = Assert.Single(
            listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureIncidentAbandoned);
        Assert.Equal(CaptureIncidentKinds.AccessLoss, Field(closer, "incident"));
        Assert.Equal(CaptureIncidents.AccessLossThreshold.ToString(), Field(closer, "count"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ARecreateIncidentOpenWhenTheLoopExitsIsClosedWithItsAttemptsAndDuration()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        incidents.RecreateFailed(1, nowTicks: 1_000);
        incidents.RecreateFailed(2, nowTicks: 3_000);
        incidents.RecreateOpened();

        incidents.AbandonOpen(7_000);

        var closer = Assert.Single(
            listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureIncidentAbandoned);
        Assert.Equal(CaptureIncidentKinds.Recreate, Field(closer, "incident"));
        Assert.Equal("2", Field(closer, "count"));
        Assert.Equal("6000", Field(closer, "duration_ms"));
    }

    [Fact]
    public void ARecreateThatEndedOnItsOwnLineLeavesNothingToClose()
    {
        using var listener = new TestEventListener("Deckle-Vision");
        var incidents = NewIncidents();

        incidents.RecreateFailed(1, nowTicks: 0);
        incidents.RecreateFailed(2, nowTicks: 2_000);
        incidents.RecreateOpened();
        incidents.RecreateEnded();

        incidents.AbandonOpen(9_000);

        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVisionSource.EvtCaptureIncidentAbandoned);
    }
}