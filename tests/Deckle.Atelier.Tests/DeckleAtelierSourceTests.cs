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
        Assert.Equal("Photos", listener.Events[1].Payload?[1]);
    }

    [Fact]
    public void ChangingAnIconIsOneMilestoneThenTheChoiceWithoutThePath()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string folder = scratch.Folder("A");
        store.Add(folder);
        using var listener = new TestEventListener("Deckle-Atelier");

        store.ChangeIcon(folder, new Deckle.Core.IconChoice(Deckle.Core.IconSymbol.Home, Deckle.Core.IconTint.Purple, true));

        Assert.Equal(
            [DeckleAtelierSource.EvtFolderIconChanged, DeckleAtelierSource.EvtFolderIconChangedDetail],
            listener.Events.Select(e => e.EventId));
        Assert.Equal(["Home", "Purple", true], listener.Events[1].Payload!);
    }

    [Fact]
    public void RemovingAFolderIsOneMilestoneThenTheCountLeft()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string a = scratch.Folder("A");
        store.Add(a);
        store.Add(scratch.Folder("B"));
        using var listener = new TestEventListener("Deckle-Atelier");

        store.Remove(a);

        Assert.Equal(
            [DeckleAtelierSource.EvtFolderRemoved, DeckleAtelierSource.EvtFolderRemovedDetail],
            listener.Events.Select(e => e.EventId));
        Assert.Equal(EventLevel.Informational, listener.Events[0].Level);
        Assert.Equal([1], listener.Events[1].Payload!);
    }

    [Fact]
    public void AnUnreachableAddedFolderIsAWarningWithItsReasonAndNoPath()
    {
        using var listener = new TestEventListener("Deckle-Atelier");

        DeckleAtelierSource.Log.AddedFolderUnreachable(nameof(AddedFolderReachability.NotFound));

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleAtelierSource.EvtAddedFolderUnreachable, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal(["NotFound"], ev.Payload!);
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
