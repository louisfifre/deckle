using Xunit;

namespace Deckle.Browser.Tests;

[Trait("Category", "unit")]
public sealed class ShortcutsTests
{
    [Fact]
    public void NoTwoCommandsShareAGesture()
    {
        var byGesture = Shortcuts.All.GroupBy(s => s.Gesture).ToList();

        Assert.All(byGesture, g => Assert.Single(g.Select(s => s.Command).Distinct()));
        // A gesture listed twice would be a copy-paste: each appears once.
        Assert.All(byGesture, g => Assert.Single(g));
    }

    [Fact]
    public void EveryCommandHasAGesture()
    {
        foreach (ShortcutCommand command in Enum.GetValues<ShortcutCommand>())
            Assert.Contains(Shortcuts.All, s => s.Command == command);
    }

    [Theory]
    [InlineData("T", true, false, false, ShortcutCommand.NewTab)]
    [InlineData("T", true, true, false, ShortcutCommand.ReopenClosedTab)]
    [InlineData("L", true, false, false, ShortcutCommand.FocusAddress)]
    [InlineData("D", false, false, true, ShortcutCommand.FocusAddress)]
    [InlineData("F5", false, false, false, ShortcutCommand.Reload)]
    [InlineData("R", true, false, false, ShortcutCommand.Reload)]
    [InlineData("Left", false, false, true, ShortcutCommand.Back)]
    [InlineData("Tab", true, true, false, ShortcutCommand.PreviousTab)]
    [InlineData("N", true, true, false, ShortcutCommand.PrivateWindow)]
    public void AGestureFindsItsCommand(string key, bool ctrl, bool shift, bool alt, ShortcutCommand expected) =>
        Assert.Equal(expected, Shortcuts.Find(new KeyGesture(key, ctrl, shift, alt)));

    [Fact]
    public void PrintHasItsGesture() =>
        Assert.Equal(ShortcutCommand.Print, Shortcuts.Find(new KeyGesture("P", Ctrl: true)));

    [Fact]
    public void AKeyThatIsNotAShortcutFindsNothing()
    {
        Assert.Null(Shortcuts.Find(new KeyGesture("T")));
        Assert.Null(Shortcuts.Find(new KeyGesture("W", Ctrl: true, Shift: true)));
    }

    [Fact]
    public void TheClosedTabsComeBackMostRecentFirstAndABlankPageIsNotKept()
    {
        var closed = new ClosedTabs();
        closed.Push("https://a.test/", "A");
        closed.Push("about:blank", "");
        closed.Push("https://b.test/", "B");

        Assert.Equal(2, closed.Count);
        Assert.Equal(("https://b.test/", "B"), closed.Pop());
        Assert.Equal(("https://a.test/", "A"), closed.Pop());
        Assert.Null(closed.Pop());
    }

    [Fact]
    public void TheClosedTabsKeepOnlyTheLastOnesBeyondTheirCapacity()
    {
        var closed = new ClosedTabs(2);
        closed.Push("https://a.test/", "A");
        closed.Push("https://b.test/", "B");
        closed.Push("https://c.test/", "C");

        Assert.Equal(2, closed.Count);
        Assert.Equal("https://c.test/", closed.Pop()!.Value.Url);
        Assert.Equal("https://b.test/", closed.Pop()!.Value.Url);
    }
}
