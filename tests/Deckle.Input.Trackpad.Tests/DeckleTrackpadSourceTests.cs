using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

[Trait("Category", "observability")]
public sealed class DeckleTrackpadSourceTests
{
    [Fact]
    public void InjectionIncidentAndRecoveryRemainPermanent()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.InjectionFailed();
        DeckleTrackpadSource.Log.InjectionRecovered();

        Assert.Collection(
            listener.Events,
            incident =>
            {
                Assert.Equal(DeckleTrackpadSource.EvtInjectionFailed, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
            },
            recovery =>
            {
                Assert.Equal(DeckleTrackpadSource.EvtInjectionRecovered, recovery.EventId);
                Assert.Equal(EventLevel.Informational, recovery.Level);
            });
    }
}
