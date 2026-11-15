using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Projects.Views;

// The flyout the info button beside "Version" opens (board P2): how many
// versions Deckle found, one line per source with its value written without
// the "v", and the sentence that says different versions can be normal.
//
// A Flyout, not a panel drawn in the page: the presenter brings the theme's own
// flyout background, border and shadow, and the light dismiss.
internal static class VersionsFlyout
{
    // The content's width; with the presenter's 16 DIP padding the flyout is
    // 380 DIP wide, as the board draws it.
    private const double ContentWidth = 348;

    public static Flyout Build(VersionReading version)
    {
        var panel = new StackPanel { Spacing = 8, Width = ContentWidth };

        TextBlock heading = new() { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        heading.Text = HeadingFor(version);
        panel.Children.Add(heading);

        var rows = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int row = 0;
        void AddRow(string label, string value)
        {
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock source = Themed.Text("TextFillColorSecondaryBrush");
            source.Text = label == VersionSource.GitTagLabel ? ProjectsText.Get("Project_GitTag") : label;
            source.TextTrimming = TextTrimming.CharacterEllipsis;
            source.IsTextSelectionEnabled = false;
            ToolTipService.SetToolTip(source, label);
            Grid.SetRow(source, row);

            TextBlock number = new() { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(number, row);
            Grid.SetColumn(number, 1);

            rows.Children.Add(source);
            rows.Children.Add(number);
            row++;
        }

        foreach (VersionSource source in version.Sources) AddRow(source.Label, source.Value);
        panel.Children.Add(rows);

        // The engine caps the manifests it lists; the rest is counted, not shown.
        if (version.MoreSources > 0)
        {
            TextBlock more = Themed.Text("TextFillColorSecondaryBrush");
            more.Text = ProjectsText.Format("Project_VersionsMore_Format", version.MoreSources);
            panel.Children.Add(more);
        }

        TextBlock footer = Themed.Text("TextFillColorSecondaryBrush", "CaptionTextBlockStyle");
        footer.Text = ProjectsText.Get("Project_VersionsFooter");
        footer.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(footer);

        return new Flyout { Content = panel, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
    }

    // "3 versions found", or "1 version found" when the only source is the one
    // the page reads without a tag.
    public static string HeadingFor(VersionReading version) =>
        version.Count == 1
            ? ProjectsText.Get("Project_VersionFoundOne")
            : ProjectsText.Format("Project_VersionsFound_Format", version.Count);
}
