using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// A section of the details panel whose content is not label and value pairs:
// the map's graph and its lists. Drawn as DetailsSheet draws its sections, a
// divider above, a title in a stronger weight with room above it (spec,
// Details panel), so the panel reads as one sheet. DetailsSheet is shared by
// the tools built in parallel and takes only pairs; if a second tool needs a
// free section, this belongs there.
internal static class MapPanelSection
{
    public static UIElement Create(string title, UIElement content)
    {
        var section = new StackPanel
        {
            Padding = new Thickness(16, 16, 16, 12),
            Spacing = 8,
            BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        section.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        section.Children.Add(content);
        return section;
    }
}
