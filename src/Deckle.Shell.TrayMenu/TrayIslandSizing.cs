// TrayIslandSizing — has the carrier's XAML island reached the client size just requested?

using System;

namespace Deckle.Shell.TrayMenu;

// Under FlyoutPlacementMode.Full, FlyoutBase sizes its presenter from the XAML
// island's live size at the moment of ShowAt (CXamlIslandRoot::GetSize, read
// through CalculateAvailableWindowRect in FlyoutBase_partial.cpp), not from the
// HWND rect the carrier has just been given, and it never re-reads it when the
// window is resized afterwards. An island still at its previous size, or at
// zero for a window never shown, leaves the presenter at its minimum: the
// one-item-tall menu of 2026-10-12. The carrier therefore opens its flyout only
// once XamlRoot reports the requested size; this type holds that comparison so
// the rule is testable without a window.
internal static class TrayIslandSizing
{
    // The island reports DIP, the carrier requests physical pixels. The two
    // sides agree to the pixel once the resize has propagated; the tolerance
    // absorbs the rounding of a conversion and a one-pixel frame on each side,
    // while a stale size (zero, or the window's former size) stays far outside it.
    internal const double ToleranceDip = 2.0;

    /// <summary>
    /// True when the island's reported size is the requested client size.
    /// </summary>
    /// <param name="requestedPx">The client size the carrier asked for, in
    /// physical pixels.</param>
    /// <param name="islandDip">The size XamlRoot reports, in DIP.</param>
    /// <param name="rasterizationScale">XamlRoot.RasterizationScale, the
    /// island's own pixels-per-DIP ratio.</param>
    internal static bool MatchesRequest(
        (int Width, int Height) requestedPx,
        (double Width, double Height) islandDip,
        double rasterizationScale)
    {
        if (rasterizationScale <= 0) return false;
        if (islandDip.Width <= 0 || islandDip.Height <= 0) return false;

        double expectedWidth = requestedPx.Width / rasterizationScale;
        double expectedHeight = requestedPx.Height / rasterizationScale;
        return Math.Abs(islandDip.Width - expectedWidth) <= ToleranceDip
            && Math.Abs(islandDip.Height - expectedHeight) <= ToleranceDip;
    }
}
