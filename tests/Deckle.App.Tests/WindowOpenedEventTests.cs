using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.App.Tests;

// A window opened reaches log.jsonl, which keeps Informational lines only when
// they carry the milestone tag, and names the window alone.
[Trait("Category", "observability")]
public sealed class WindowOpenedEventTests
{
    [Fact]
    public void WindowOpenedIsAMilestoneNamingOnlyTheWindow()
    {
        using var listener = new TestEventListener("Deckle-App");

        DeckleAppSource.Log.WindowOpened("log");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleAppSource.EvtWindowOpened);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Milestone, ev.Tags & ObservationTags.Milestone);
        Assert.Equal(new object?[] { "log" }, ev.Payload);
    }
}