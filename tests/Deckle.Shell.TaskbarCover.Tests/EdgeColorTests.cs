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

    // Measured 2026-11-26: T3 Code's edge went from #232325 to #323232 as it
    // lost the focus under a window being maximized over it.
    [Fact]
    public void AnEdgeDimmingAsItLosesTheFocusIsNotFarAndAnotherWindowsEdgeIs()
    {
        Assert.False(EdgeColor.Far(0x00252323u, 0x00323232u));
        Assert.True(EdgeColor.Far(0x00252323u, 0x00F0F0F0u));
    }

    [Fact]
    public void APlainEdgeCoversTheWholeLineAndAVariedOneDoesNot()
    {
        EdgeColor.Dominant(Enumerable.Repeat(0xFF000000u, 100).ToArray(), out double plain);
        // A blurred wallpaper: shades far enough apart to fall in several groups.
        uint[] blurred = [.. Enumerable.Range(0, 100).Select(i => 0xFF000000u | (uint)(i * 2) << 16 | (uint)(i * 2) << 8)];
        EdgeColor.Dominant(blurred, out double varied);

        Assert.True(plain >= EdgeColor.PlainShare);
        Assert.True(varied < EdgeColor.PlainShare);
    }
}
