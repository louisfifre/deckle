using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// The words of a Home section's header: its title in the subtitle style and the
// number of its projects beside it in the tertiary text colour. A category and
// the "Without a category" fold wear this same label, so the two groups of
// cards read as the same kind of section; what differs is the glyph after it,
// which says what pressing the header does (a chevron to the right opens the
// category's page, a chevron up or down opens or closes the fold), the way
// Windows tells a link from a disclosure.
internal sealed class SectionHeaderLabel : StackPanel
{
    private readonly TextBlock _title;
    private readonly TextBlock _count;

    public SectionHeaderLabel()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;

        _title = new TextBlock
        {
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        _count = Themed.Text("TextFillColorTertiaryBrush", "BodyTextBlockStyle");
        _count.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_title);
        Children.Add(_count);
    }

    public void Set(string title, int count)
    {
        _title.Text = title;
        _count.Text = count.ToString(ProjectsText.Culture);
    }
}
