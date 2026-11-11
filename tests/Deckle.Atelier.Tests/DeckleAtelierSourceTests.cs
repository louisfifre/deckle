using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Atelier.Tests;

// The provider says what happened to the added folders and the listings
// without ever carrying a path. Each test subscribes its own listener; the
// listing and the store emit on the same singleton, so the suites that call
// them share this collection.
[Collection("atelier provider singleton")]
[Trait("Category", "observability")]
public sealed class DeckleAtelierSourceTests
{
    [Fact]
    public void AddingAFolderIsOneMilestoneThenItsCount()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string photos = scratch.Folder("Photos");
        using var listener = new TestEventListener("Deckle-Atelier");

        store.Add(photos);

        Assert.Equal(
            [DeckleAtelierSource.EvtFolderAdded, DeckleAtelierSource.EvtFolderAddedDetail],
            listener.Events.Select(e => e.EventId));
        Assert.Equal(1, listener.Events[1].Payload?[0]);
    }

    [Fact]
    public void AListingThatFailsIsAWarningWithItsReasonAndNoPath()
    {
        using var scratch = new ScratchFolder();
        using var listener = new TestEventListener("Deckle-Atelier");

        FolderListing.List(Path.Combine(scratch.Path, "gone"));

        var ev = Assert.Single(listener.Events);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("NotFound", ev.Payload?[0]);
    }
}
