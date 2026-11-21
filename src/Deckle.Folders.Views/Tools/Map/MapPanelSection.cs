using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// A section of the details panel whose content is not label and value pairs:
// the map's graph and its lists. Drawn as DetailsSheet draws its sections, a
// divider above, a title in a stronger weight with room above it (spec,
// Details panel), so the panel reads as one sheet. DetailsSheet is shared by
// the tools built in parallel and takes only pairs; if a second tool needs a
// free section, this belongs there.
//
// A section can carry one action at the end of its title's line, as the
// graph's enlarge button sits next to its title (spec, Map).
internal static class MapPanelSection
{
    public static UIElement Create(string title, UIElement content, UIElement? action = null)
    {
        var section = new StackPanel
        {
            Padding = new Thickness(16, 16, 16, 12),
            Spacing = 8,
            BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        var heading = new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (action is null)
        {
            section.Children.Add(heading);
        }
        else
        {
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(heading);
            if (action is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn((FrameworkElement)action, 1);
            line.Children.Add(action);
            section.Children.Add(line);
        }
        section.Children.Add(content);
        return section;
    }
}
