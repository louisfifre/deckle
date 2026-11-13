using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Settings.Tests;

// A page opened reaches log.jsonl, which keeps Informational lines only when
// they carry the milestone tag, and names the page alone.
[Trait("Category", "observability")]
public sealed class PageOpenedEventTests
{
    [Fact]
    public void PageOpenedIsAMilestoneNamingOnlyThePage()
    {
        using var listener = new TestEventListener("Deckle-Settings");

        DeckleSettingsSource.Log.PageOpened("DiagnosticsPage");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleSettingsSource.EvtPageOpened);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Milestone, ev.Tags & ObservationTags.Milestone);
        Assert.Equal(new object?[] { "DiagnosticsPage" }, ev.Payload);
    }
}