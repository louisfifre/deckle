// TrayMenuSizing — the size decision for the context menu's carrier window.

using System;
using System.Collections.Generic;

namespace Deckle.Shell.TrayMenu;

// Under FlyoutPlacementMode.Full the visible menu card takes the carrier's
// size exactly. A carrier smaller than the items turns on the presenter's
// scroll viewer, and the presenter's MinHeight (MenuFlyoutThemeMinHeight,
// 32 DIP) leaves a card one item tall that the user can scroll. The host reads
// the WinUI measurements; this type only decides, so the rule is testable
// without a XAML tree.
//
// Rule: the captured MenuFlyoutPresenter size is preferred, because it carries
// the presenter's real padding and border. It is trusted only when it holds
// every item: a presenter measured clipped, or before its items were laid out,
// reports its MinHeight, which is positive. Otherwise the carrier takes the
// item extent plus a fixed frame margin.
internal static class TrayMenuSizing
{
    // Margin per side added to the item extent when the presenter size is not
    // trusted. Deliberately above the presenter's real chrome: a card a few
    // DIP too large shows a thin empty band, a card too small scrolls.
    internal const double FallbackFrameMargin = 4.0;

    internal enum SizeSource
    {
        // The captured presenter size, checked against the items.
        Presenter,
        // The item extent plus FallbackFrameMargin on each side.
        Items,
    }

    internal readonly record struct CarrierSize(double Width, double Height, SizeSource Source);

    /// <summary>
    /// Decides the carrier size in DIP.
    /// </summary>
    /// <param name="capturedPresenter">The MenuFlyoutPresenter DesiredSize
    /// captured with the items attached, or null when none was captured.</param>
    /// <param name="items">The DesiredSize of every item, separators
    /// included.</param>
    internal static CarrierSize Decide(
        (double Width, double Height)? capturedPresenter,
        IReadOnlyList<(double Width, double Height)> items)
    {
        var (itemsWidth, itemsHeight) = ItemExtent(items);

        if (capturedPresenter is { } presenter
            && HoldsItems(presenter, itemsWidth, itemsHeight))
        {
            return new CarrierSize(presenter.Width, presenter.Height, SizeSource.Presenter);
        }

        return new CarrierSize(
            itemsWidth + FallbackFrameMargin * 2,
            itemsHeight + FallbackFrameMargin * 2,
            SizeSource.Items);
    }

    internal static bool HoldsItems(
        (double Width, double Height) presenter,
        IReadOnlyList<(double Width, double Height)> items)
    {
        var (itemsWidth, itemsHeight) = ItemExtent(items);
        return HoldsItems(presenter, itemsWidth, itemsHeight);
    }

    private static bool HoldsItems(
        (double Width, double Height) presenter, double itemsWidth, double itemsHeight)
        => presenter.Width > 0
            && presenter.Height > 0
            && presenter.Width >= itemsWidth
            && presenter.Height >= itemsHeight;

    internal static (double Width, double Height) ItemExtent(
        IReadOnlyList<(double Width, double Height)> items)
    {
        double width = 0;
        double height = 0;
        foreach (var (w, h) in items)
        {
            width = Math.Max(width, w);
            height += h;
        }
        return (width, height);
    }
}
