using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// The inside of the details panel, as board 12.1 draws it: the kind of item
// in small secondary text, its name as the panel's title, then sections, each
// a title above label and value pairs. The section titles stand out from what
// they hold, by weight and by the room above them (spec, Details panel), and
// a divider separates the sections. Every tool builds its panel with it, so
// panels look alike whatever the tool.
public sealed class DetailsSheet
{
    private readonly StackPanel _root = new();

    public DetailsSheet(string kind, string title)
    {
        var head = new StackPanel { Padding = new Thickness(16, 16, 16, 12), Spacing = 2 };
        head.Children.Add(new TextBlock
        {
            Text = kind,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
        });
        head.Children.Add(new TextBlock
        {
            Text = title,
            Style = Resource<Style>("SubtitleTextBlockStyle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });
        ToolTipService.SetToolTip(head.Children[1], title);
        _root.Children.Add(head);
    }

    public UIElement Element => _root;

    // A section with its title and label/value rows; a null value skips its row.
    public DetailsSheet Section(string title, params (string Label, string? Value)[] rows)
    {
        var section = new StackPanel
        {
            Padding = new Thickness(16, 16, 16, 12),
            Spacing = 8,
            BorderBrush = Resource<Brush>("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        section.Children.Add(new TextBlock { Text = title, Style = Resource<Style>("BodyStrongTextBlockStyle") });

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        foreach ((string label, string? value) in rows)
        {
            if (value is null) continue;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock
            {
                Text = label,
                Style = Resource<Style>("CaptionTextBlockStyle"),
                Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            };
            var v = new TextBlock
            {
                Text = value,
                Style = Resource<Style>("CaptionTextBlockStyle"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            Grid.SetRow(l, row);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            row++;
        }
        section.Children.Add(grid);
        _root.Children.Add(section);
        return this;
    }

    // A section whose content is not label/value pairs (the photo tool's mark
    // buttons), framed and titled as the others so the panel reads as one.
    public DetailsSheet Section(string title, UIElement content)
    {
        var section = new StackPanel
        {
            Padding = new Thickness(16, 16, 16, 12),
            Spacing = 8,
            BorderBrush = Resource<Brush>("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        section.Children.Add(new TextBlock { Text = title, Style = Resource<Style>("BodyStrongTextBlockStyle") });
        section.Children.Add(content);
        _root.Children.Add(section);
        return this;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
