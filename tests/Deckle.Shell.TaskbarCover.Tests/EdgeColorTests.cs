using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-11-26): beside a maximized app, the place
// of a faded taskbar continues the colour of the app's edge that touches it.
[Trait("Category", "unit")]
public class EdgeColorTests
{

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
