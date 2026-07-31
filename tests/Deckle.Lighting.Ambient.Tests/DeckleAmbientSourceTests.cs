using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Lighting.Ambient;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "observability")]
public class DeckleAmbientSourceTests
{
    public DeckleAmbientSourceTests()
        => OperationalLogAdmission.Configure(
            activity => activity == OperationalLogActivity.Ambient);

    [Fact]
    public void ExternalChangeStoppedSeparatesInfoFromTechnicalDetail()
    {
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.ExternalChangeStopped();
        DeckleAmbientSource.Log.ExternalChangeStoppedDetail(
            v1_id: "4",
            resource_type: "light",
            age_ms: 1200,
            on: "true",
            bri: "42",
            xy: "0.3127,0.3290");

        Assert.Equal(2, listener.Events.Count);
        Assert.Equal(DeckleAmbientSource.EvtExternalChangeStopped, listener.Events[0].EventId);
        Assert.Equal(EventLevel.Informational, listener.Events[0].Level);
        Assert.True(listener.Events[0].Payload is null || listener.Events[0].Payload!.Count == 0);

        Assert.Equal(DeckleAmbientSource.EvtExternalChangeStoppedDetail, listener.Events[1].EventId);
        Assert.Equal(EventLevel.Verbose, listener.Events[1].Level);
        Assert.True(listener.Events[1].HasKeyword(Keywords.Lifecycle));
        Assert.Equal("4", listener.Events[1].Payload?[0]);
        Assert.Equal("light", listener.Events[1].Payload?[1]);
        Assert.Equal(1200, listener.Events[1].Payload?[2]);
    }

    [Fact]
    public void EchoIgnoredEmitsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.EchoIgnored(
            v1_id: "5",
            resource_type: "light",
            age_ms: 1100);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleAmbientSource.EvtEchoIgnored, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Lifecycle));
        Assert.Equal("5", ev.Payload?[0]);
        Assert.Equal("light", ev.Payload?[1]);
        Assert.Equal(1100, ev.Payload?[2]);
    }

    [Fact]
    public void HeartbeatNamesPushStatsInsteadOfHttpStats()
    {
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.Heartbeat(
            mode: "multi",
            period_sec: 5.0,
            target_hz: 50,
            effective_hz: 49.8,
            ticks: 46,
            pushed: 12,
            dropped: 34,
            skipped_slots: 1,
            unmapped_lights: 0,
            push_stats_suffix: " | push_avg_ms=0.1 | push_p95_ms=0.2 | push_max_ms=0.3");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleAmbientSource.EvtHeartbeat, ev.EventId);
        Assert.Equal(50, ev.Payload?[2]);
        Assert.Equal(49.8, ev.Payload?[3]);
        Assert.Equal(1L, ev.Payload?[7]);
        Assert.Equal("push_stats_suffix", ev.PayloadNames?[9]);
        var suffix = Assert.IsType<string>(ev.Payload?[9]);
        Assert.Contains("push_avg_ms", suffix);
        Assert.DoesNotContain("http_avg_ms", suffix);
    }

    [Fact]
    public void HeartbeatIsRejectedWhenAmbientDetailIsDisabled()
    {
        OperationalLogAdmission.Configure(_ => false);
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.Heartbeat("group", 5, 15, 15, 75, 2, 73, 0, 0, "");

        Assert.Empty(listener.Events);
    }

    [Fact]
    public void FrameProcessingEpisodeFormsOneOperationalNarrative()
    {
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.FrameProcessingIncidentOpened();
        DeckleAmbientSource.Log.FrameProcessingRecovered();
        DeckleAmbientSource.Log.FrameProcessingFailed();

        Assert.Collection(
            listener.Events,
            incident =>
            {
                Assert.Equal(DeckleAmbientSource.EvtFrameProcessingIncidentOpened, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
            },
            recovery =>
            {
                Assert.Equal(DeckleAmbientSource.EvtFrameProcessingRecovered, recovery.EventId);
                Assert.Equal(EventLevel.Informational, recovery.Level);
            },
            fatal =>
            {
                Assert.Equal(DeckleAmbientSource.EvtFrameProcessingFailed, fatal.EventId);
                Assert.Equal(EventLevel.Error, fatal.Level);
            });
    }
}
