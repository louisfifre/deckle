using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.Shell.TrayMenu.Tests;

// Behaviour of TrayIslandSizing: a tray surface opens its flyout only once the
// carrier's XAML island reports the client size the carrier asked for, because
// Placement Full sizes the presenter from that island size at ShowAt. Sizes are
// the context menu's as it ran on 2026-10-13: 102 x 171 DIP at 200 %.
public class TrayIslandSizingTests
{
    private const double Scale = 2.0;
    private static readonly (int Width, int Height) RequestedPx = (204, 342);

    // Regression: on the first open after launch the island of a carrier never
    // shown still reported no size, the presenter took it and fell to its
    // MinHeight, and the menu opened one item tall until a second click.
    [Fact]
    [Trait("Category", "regression")]
    public void AnIslandStillAtZeroDoesNotHoldTheRequest()
    {
        Assert.False(TrayIslandSizing.MatchesRequest(RequestedPx, (0, 0), Scale));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AnIslandAtTheRequestedSizeHoldsIt()
    {
        Assert.True(TrayIslandSizing.MatchesRequest(RequestedPx, (102, 171), Scale));
    }

    // The carrier rounds DIP up to whole pixels; the island converts them back
    // to DIP. A fractional difference, or a one-pixel frame, is the same size.
    [Fact]
    [Trait("Category", "unit")]
    public void ARoundingOrAOnePixelFrameIsStillTheRequestedSize()
    {
        var oddPx = (205, 343);

        Assert.True(TrayIslandSizing.MatchesRequest(oddPx, (102.5, 171.5), Scale));
        Assert.True(TrayIslandSizing.MatchesRequest(oddPx, (101.5, 170.5), Scale));
        Assert.True(TrayIslandSizing.MatchesRequest((137, 229), (91.33, 152.67), 1.5));
    }

    // The window's former size, kept by an island that has not processed the
    // resize yet, is not the requested one, whether larger or smaller.
    [Fact]
    [Trait("Category", "unit")]
    public void AnIslandStillAtItsFormerSizeDoesNotHoldTheRequest()
    {
        Assert.False(TrayIslandSizing.MatchesRequest(RequestedPx, (512, 384), Scale));
        Assert.False(TrayIslandSizing.MatchesRequest(RequestedPx, (102, 160), Scale));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AnUnknownScaleNeverHoldsTheRequest()
    {
        Assert.False(TrayIslandSizing.MatchesRequest(RequestedPx, (102, 171), 0));
    }
}
