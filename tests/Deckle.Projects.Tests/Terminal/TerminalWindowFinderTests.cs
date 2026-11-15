using Xunit;

namespace Deckle.Projects.Tests;

// The finder on a list of windows the test gives it: the title decides, a
// minimised window is restored before the foreground call.
[Trait("Category", "unit")]
public sealed class TerminalWindowFinderTests
{
    private const string Title = "Build & run · alpha (1a2b)";

    [Fact]
    public void TheWindowWhoseTitleMatchesIsBroughtToTheFront()
    {
        var windows = new FakeWindowSystem();
        windows.Windows.AddRange([new TopWindow(1, "Notepad"), new TopWindow(2, Title), new TopWindow(3, Title + " extra")]);

        bool shown = new TerminalWindowFinder(windows).Show(Title);

        Assert.True(shown);
        Assert.Equal(["front 2"], windows.Calls);
    }

    [Fact]
    public void AMinimisedWindowIsRestoredBeforeTheForegroundCall()
    {
        var windows = new FakeWindowSystem();
        windows.Windows.Add(new TopWindow(2, Title));
        windows.Minimized.Add(2);

        new TerminalWindowFinder(windows).Show(Title);

        Assert.Equal(["restore 2", "front 2"], windows.Calls);
    }

    [Fact]
    public void NoMatchingWindowGivesFalseAndTouchesNothing()
    {
        var windows = new FakeWindowSystem();
        windows.Windows.Add(new TopWindow(1, "Notepad"));

        Assert.False(new TerminalWindowFinder(windows).Show(Title));
        Assert.Empty(windows.Calls);
    }

    [Fact]
    public void ATitleWithASemicolonIsMatchedTheWayTheLaunchWroteIt()
    {
        var windows = new FakeWindowSystem();
        windows.Windows.Add(new TopWindow(5, "Build, run · a, b (0000)"));

        Assert.True(new TerminalWindowFinder(windows).Show("Build; run · a; b (0000)"));
    }
}
