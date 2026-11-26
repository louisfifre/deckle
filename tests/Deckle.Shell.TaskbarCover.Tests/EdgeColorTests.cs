using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-11-26): beside a maximized app, the place
// of a faded taskbar continues the colour of the app's edge that touches it.
[Trait("Category", "unit")]
public class EdgeColorTests
{
    private static readonly NativeMethods.RECT LeftBar = new() { left = 0, top = 0, right = 32, bottom = 1080 };

    [Fact]
    public void ALeftTaskbarReadsTheAppsFirstColumn()
    {
        var line = EdgeColor.SampleLine(LeftBar, TaskbarEdge.Left);

        Assert.Equal((32, 0, 33, 1080), (line.left, line.top, line.right, line.bottom));
    }

    [Fact]
    public void ABottomTaskbarReadsTheAppsLastRow()
    {
        var bar = new NativeMethods.RECT { left = 0, top = 1032, right = 1920, bottom = 1080 };

        var line = EdgeColor.SampleLine(bar, TaskbarEdge.Bottom);

        Assert.Equal((0, 1031, 1920, 1032), (line.left, line.top, line.right, line.bottom));
    }

    [Fact]
    public void APlainEdgeGivesItsOwnColour()
    {
        uint[] pixels = Enumerable.Repeat(0xFF2B5797u, 100).ToArray();

        // 0xAARRGGBB in, COLORREF 0x00BBGGRR out.
        Assert.Equal(0x0097572Bu, EdgeColor.Dominant(pixels));
    }

    [Fact]
    public void TheColourMostOfTheEdgeShowsWinsOverAFewOthers()
    {
        // A dark sidebar with a few bright icons on it.
        uint[] pixels = [.. Enumerable.Repeat(0xFF202020u, 90), .. Enumerable.Repeat(0xFFFFFFFFu, 10)];

        Assert.Equal(0x00202020u, EdgeColor.Dominant(pixels));
    }

    [Fact]
    public void ANoisyEdgeGivesTheMeanOfItsLargestGroup()
    {
        // A blurred panel: close shades of one colour, not one exact pixel value.
        uint[] pixels = [0xFFE0A080u, 0xFFE2A282u, 0xFFE4A484u, 0xFF101010u];

        Assert.Equal(0x0082A2E2u, EdgeColor.Dominant(pixels));
    }

    [Fact]
    public void TheSameEdgeReadTwiceIsCloseAndAnotherColourIsNot()
    {
        Assert.True(EdgeColor.Close(0x00272757u, 0x002B2B5Au));
        Assert.False(EdgeColor.Close(0x00272757u, 0x0097572Bu));
    }
}
