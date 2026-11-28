using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Deckle.Browser.Views;

// Rounds what a Border shows, the web view included. A Border's CornerRadius
// rounds its own background and stroke but does not clip its child, and the
// WebView2 draws its page in a composition visual of its own: seen in the
// self-check of 2026-11-28, the page's corners stayed square, and a rounded
// clip on the Border's visual left them square too. The same clip on the
// WebView2 element's own visual rounds them: seen in the next capture.
internal static class RoundedClip
{
    /// <summary>Clips the content to the card's corners. The returned action
    /// takes the clip off the card's events, for a view that is closed.</summary>
    public static Action Attach(Border card, FrameworkElement content)
    {
        void OnSize(object sender, SizeChangedEventArgs e) => Apply(card, content);
        card.SizeChanged += OnSize;
        content.SizeChanged += OnSize;
        return () =>
        {
            card.SizeChanged -= OnSize;
            content.SizeChanged -= OnSize;
        };
    }

    private static void Apply(Border card, FrameworkElement content)
    {
        var visual = ElementCompositionPreview.GetElementVisual(content);
        var compositor = visual.Compositor;
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2((float)content.ActualWidth, (float)content.ActualHeight);
        float radius = (float)card.CornerRadius.TopLeft;
        geometry.CornerRadius = new Vector2(radius, radius);
        visual.Clip = compositor.CreateGeometricClip(geometry);
    }
}
