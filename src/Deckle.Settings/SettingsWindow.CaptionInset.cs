using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Settings;

// ─── Caption-inset correction ─────────────────────────────────────────────────
//
// The WinAppSDK 1.8 TitleBar control stamps its two padding columns with the
// AppWindowTitleBar insets' RAW PHYSICAL pixels (UpdatePadding in microsoft-ui-xaml
// TitleBar.cpp: GridLengthHelper::FromPixels(RightInset()), no scale division), so
// at 150/200 % display scale the bar reserves 1.5–2× the room the caption buttons
// actually take — RightHeader drifts far off the right edge and the central zone
// starves. Measured here at 200 %: 288 DIPs reserved where 144 suffice.
//
// Correction: re-stamp both columns in DIPs. The control only writes them in
// OnApplyTemplate and on flow-direction change — never on resize (their TODO
// 50724421) — so re-deriving on Loaded and on XamlRoot.Changed (size, scale and
// visibility transitions) does not fight a live writer, and the write is
// idempotent. Delete this file when the SDK divides by the rasterization scale;
// the correction then computes the value the control already set.

public sealed partial class SettingsWindow
{
    private void InitializeCaptionInsetFix()
    {
        AppTitleBar.Loaded += (_, _) =>
        {
            CorrectCaptionInsets();
            if (Content.XamlRoot is { } root)
                root.Changed += (_, _) => CorrectCaptionInsets();
        };
    }

    private void CorrectCaptionInsets()
    {
        // The template's layout root is the TitleBar's single visual child; its
        // first and last columns are LeftPaddingColumn / RightPaddingColumn.
        if (VisualTreeHelper.GetChildrenCount(AppTitleBar) == 0 ||
            VisualTreeHelper.GetChild(AppTitleBar, 0) is not Grid layoutRoot ||
            layoutRoot.ColumnDefinitions.Count < 2)
        {
            return;
        }

        double scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0) return;

        // Same inset-to-side mapping as the control's own UpdatePadding.
        bool leftToRight = layoutRoot.FlowDirection == FlowDirection.LeftToRight;
        double leftDips = (leftToRight ? AppWindow.TitleBar.LeftInset
                                       : AppWindow.TitleBar.RightInset) / scale;
        double rightDips = (leftToRight ? AppWindow.TitleBar.RightInset
                                        : AppWindow.TitleBar.LeftInset) / scale;

        layoutRoot.ColumnDefinitions[0].Width = new GridLength(leftDips);
        layoutRoot.ColumnDefinitions[^1].Width = new GridLength(rightDips);
    }
}
