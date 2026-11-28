using Xunit;

namespace Deckle.Browser.Tests;

[Trait("Category", "unit")]
public sealed class BrowserSessionTests
{
    [Fact]
    public void OpeningATabAddsItAtTheEndAndSelectsIt()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        Assert.Equal([a, b], s.Tabs);
        Assert.Equal(b.Id, s.SelectedId);
    }

    [Fact]
    public void OpeningATabAtAPositionInsertsIt()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        var c = s.OpenTab("c", at: 1);
        Assert.Equal([a, c, b], s.Tabs);
        Assert.Equal(c.Id, s.SelectedId);
    }

    [Fact]
    public void ClosingTheSelectedTabSelectsTheOneBelow()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        var c = s.OpenTab("c");
        s.Select(b.Id);
        s.Close(b.Id);
        Assert.Equal([a, c], s.Tabs);
        Assert.Equal(c.Id, s.SelectedId);
    }

    [Fact]
    public void ClosingTheLastSelectedTabSelectsTheOneAbove()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        s.Close(b.Id);
        Assert.Equal(a.Id, s.SelectedId);
    }

    [Fact]
    public void ClosingTheOnlyTabSelectsNothing()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        s.Close(a.Id);
        Assert.Null(s.SelectedId);
        Assert.Empty(s.Tabs);
    }

    [Fact]
    public void ClosingAnUnselectedTabKeepsTheSelection()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        s.Close(a.Id);
        Assert.Equal(b.Id, s.SelectedId);
    }

    [Fact]
    public void MovingATabReordersTheList()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        var c = s.OpenTab("c");
        s.Move(a.Id, 2);
        Assert.Equal([b, c, a], s.Tabs);
        s.Move(a.Id, 0);
        Assert.Equal([a, b, c], s.Tabs);
    }

    [Fact]
    public void PinningMakesTheTabAPinnedSiteAtTheEndKeepingItsId()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        s.Pin(a.Id);
        s.Pin(b.Id);
        Assert.Equal([a, b], s.Pinned);
        Assert.Empty(s.Tabs);
        Assert.True(a.IsPinned);
        Assert.Equal(b.Id, s.SelectedId);
    }

    [Fact]
    public void UnpinningMakesTheSiteATabAtTheTop()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        s.Pin(a.Id);
        s.Unpin(a.Id);
        Assert.Equal([a, b], s.Tabs);
        Assert.Empty(s.Pinned);
        Assert.False(a.IsPinned);
    }

    [Fact]
    public void SelectingAPinnedSiteSelectsIt()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        s.OpenTab("b");
        s.Pin(a.Id);
        s.Select(a.Id);
        Assert.Equal(a.Id, s.SelectedId);
    }

    [Fact]
    public void SelectingAnUnknownIdChangesNothing()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("a");
        s.Select(Guid.NewGuid());
        Assert.Equal(a.Id, s.SelectedId);
    }

    [Fact]
    public void EveryChangeRaisesChangedOnce()
    {
        var s = new BrowserSession();
        int raised = 0;
        s.Changed += (_, _) => raised++;
        var a = s.OpenTab("a");
        var b = s.OpenTab("b");
        s.Select(a.Id);
        s.Move(a.Id, 1);
        s.Pin(a.Id);
        s.Unpin(a.Id);
        s.Close(b.Id);
        Assert.Equal(7, raised);
    }
}
