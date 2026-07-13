using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Shell.WindowChrome;

// ─── Caption-inset correction ─────────────────────────────────────────────────
//
// The WinAppSDK 1.8 TitleBar control stamps its two padding columns with the
// AppWindowTitleBar insets' RAW PHYSICAL pixels (UpdatePadding in microsoft-ui-xaml
// TitleBar.cpp: GridLengthHelper::FromPixels(RightInset()), no scale division), so
// at 150/200 % display scale the bar reserves 1.5–2× the room the caption buttons
// actually take — RightHeader drifts far off the right edge and the central zone
// starves. Measured on SettingsWindow at 200 %: 288 DIPs reserved where 144 suffice.
//
// Correction: re-stamp both columns in DIPs. The control only writes them in
// OnApplyTemplate and on flow-direction change — never on resize (their TODO
// 50724421) — so re-deriving on Loaded and on XamlRoot.Changed (size, scale and
// visibility transitions) does not fight a live writer, and the write is
// idempotent. Delete this file when the SDK divides by the rasterization scale;
// the correction then computes the value the control already set.

public static class CaptionInsetCorrection
{
    /// <summary>Keeps the TitleBar's caption padding columns correct across
    /// scale and size changes for the window's whole lifetime.</summary>
    public static void Attach(TitleBar titleBar, AppWindow appWindow)
    {
        titleBar.Loaded += (_, _) =>
        {
            Correct(titleBar, appWindow);
            if (titleBar.XamlRoot is { } root)
                root.Changed += (_, _) => Correct(titleBar, appWindow);
        };
    }

    private static void Correct(TitleBar titleBar, AppWindow appWindow)
    {
        // The template's layout root is the TitleBar's single visual child; its
        // first and last columns are LeftPaddingColumn / RightPaddingColumn.
        if (VisualTreeHelper.GetChildrenCount(titleBar) == 0 ||
            VisualTreeHelper.GetChild(titleBar, 0) is not Grid layoutRoot ||
            layoutRoot.ColumnDefinitions.Count < 2)
        {
            return;
        }

        double scale = titleBar.XamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0) return;

        // Same inset-to-side mapping as the control's own UpdatePadding.
        bool leftToRight = layoutRoot.FlowDirection == FlowDirection.LeftToRight;
        double leftDips = (leftToRight ? appWindow.TitleBar.LeftInset
                                       : appWindow.TitleBar.RightInset) / scale;
        double rightDips = (leftToRight ? appWindow.TitleBar.RightInset
                                        : appWindow.TitleBar.LeftInset) / scale;

        layoutRoot.ColumnDefinitions[0].Width = new GridLength(leftDips);
        layoutRoot.ColumnDefinitions[^1].Width = new GridLength(rightDips);
    }
}
