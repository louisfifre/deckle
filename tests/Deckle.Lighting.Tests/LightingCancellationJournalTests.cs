using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Tests;

// A cancelled opening is a normal end: Informational, and in the journal so the
// journal shows how the attempt ended without it being a warning.
[Trait("Category", "observability")]
[Collection(LightingObservabilityCollection.Name)]
public sealed class LightingCancellationJournalTests
{
    [Fact]
    public void ACancelledEntertainmentOpeningIsInformationalAndJournaled()
    {
        using var listener = new TestEventListener("Deckle-Lighting");

        DeckleLightingSource.Log.EntertainmentConnectCancelled("handshake", 120);

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleLightingSource.EvtEntertainmentConnectCancelled);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Journal, ev.Tags & ObservationTags.Journal);
    }
}