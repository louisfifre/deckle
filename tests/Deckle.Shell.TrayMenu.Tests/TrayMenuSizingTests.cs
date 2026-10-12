using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.Shell.TrayMenu.Tests;

// Behaviour of TrayMenuSizing: under Placement Full the carrier size is the
// visible menu card, so the carrier must never be smaller than the items it
// shows. Item sizes are the narrow-density menu as logged on 2026-07-16:
// five items of 32 DIP and one separator of 3 DIP.
public class TrayMenuSizingTests
{
    private static readonly (double Width, double Height)[] Items =
    [
        (59.0, 32.0),   // Logs
        (80.0, 32.0),   // Settings
        (101.0, 32.0),  // Playground
        (0.0, 3.0),     // separator
        (73.0, 32.0),   // Restart
        (57.0, 32.0),   // Quit
    ];

    private static readonly (double Width, double Height) ItemExtent =
        TrayMenuSizing.ItemExtent(Items);

    // Regression: a presenter measured clipped, or before its items were laid
    // out, reports its MinHeight (one item, 32 DIP). The carrier used to
    // accept any positive captured size, so the menu opened one item tall
    // and scrolled until the process restarted.
    [Fact]
    [Trait("Category", "regression")]
    public void CapturedSizeSmallerThanItsItemsIsNeverUsed()
    {
        var oneItemTall = (ItemExtent.Width + 2, 32.0);

        var size = TrayMenuSizing.Decide(oneItemTall, Items);

        Assert.Equal(TrayMenuSizing.SizeSource.Items, size.Source);
        Assert.True(size.Height >= ItemExtent.Height);
        Assert.True(size.Width >= ItemExtent.Width);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CapturedSizeHoldingItsItemsIsUsedAsIs()
    {
        var presenter = (ItemExtent.Width + 2, ItemExtent.Height + 6);

        var size = TrayMenuSizing.Decide(presenter, Items);

        Assert.Equal(TrayMenuSizing.SizeSource.Presenter, size.Source);
        Assert.Equal(presenter, (size.Width, size.Height));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CapturedSizeNarrowerThanItsItemsIsNeverUsed()
    {
        var tooNarrow = (ItemExtent.Width - 10, ItemExtent.Height + 6);

        var size = TrayMenuSizing.Decide(tooNarrow, Items);

        Assert.Equal(TrayMenuSizing.SizeSource.Items, size.Source);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void WithoutCaptureTheItemsAndTheirFrameMarginSizeTheCarrier()
    {
        var size = TrayMenuSizing.Decide(null, Items);

        Assert.Equal(TrayMenuSizing.SizeSource.Items, size.Source);
        Assert.Equal(ItemExtent.Width + TrayMenuSizing.FallbackFrameMargin * 2, size.Width);
        Assert.Equal(ItemExtent.Height + TrayMenuSizing.FallbackFrameMargin * 2, size.Height);
    }

    // Regression: the quick-actions panel has one content element, not items.
    // Its capture used to accept any positive presenter size, the same "> 0"
    // check that let the menu open one item tall.
    [Fact]
    [Trait("Category", "regression")]
    public void APresenterSmallerThanThePanelContentIsNotTrusted()
    {
        var content = (Width: 220.0, Height: 296.0);
        var presenterAtItsMinimum = (96.0, 40.0);
        var presenterAroundTheContent = (content.Width + 24, content.Height + 24);

        Assert.False(TrayMenuSizing.HoldsItems(presenterAtItsMinimum, [content]));
        Assert.True(TrayMenuSizing.HoldsItems(presenterAroundTheContent, [content]));
    }
}
