using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Setup.Tests;

// A cancelled download is a normal end: Informational, and in the log next to
// the failed sibling, so the log shows how every item ended.
[Trait("Category", "observability")]
public sealed class SetupCancellationLogTests
{
    [Fact]
    public void ACancelledItemDownloadIsInformationalAndAMilestone()
    {
        using var listener = new TestEventListener("Deckle-Setup");

        DeckleSetupSource.Log.ItemCancelled();

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleSetupSource.EvtItemCancelled);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Milestone, ev.Tags & ObservationTags.Milestone);
    }
}