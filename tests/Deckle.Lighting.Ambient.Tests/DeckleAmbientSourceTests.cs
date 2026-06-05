using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Lighting.Ambient;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "observability")]
public class DeckleAmbientSourceTests
{
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
}
