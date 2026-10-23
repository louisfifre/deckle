using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Setup.Tests;

// A cancelled download is a normal end: Informational, and in the journal next to
// the failed sibling, so the journal shows how every item ended.
[Trait("Category", "observability")]
public sealed class SetupCancellationJournalTests
{
    [Fact]
    public void ACancelledItemDownloadIsInformationalAndJournaled()
    {
        using var listener = new TestEventListener("Deckle-Setup");

        DeckleSetupSource.Log.ItemCancelled();

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleSetupSource.EvtItemCancelled);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Journal, ev.Tags & ObservationTags.Journal);
    }
}