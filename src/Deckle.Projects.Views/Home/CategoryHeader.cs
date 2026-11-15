using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// The line above a category's cards on Home (board H1): its title, the number
// of its projects, and a chevron. Clicking it opens the category's page (spec:
// "clicking the category's title opens its page").
//
// A standard subtle button holds the three, so the keyboard reaches it, Enter
// and Space press it and a screen reader reads it as a link to the page. Right
// after "New category" the title is a text box to type in instead
// (BeginRename), and the button comes back once the box closes.
internal sealed class CategoryHeader : UserControl
{
    private readonly Button _open;
    private readonly InlineNameEditor _editor;

    public CategoryHeader(ProjectCategory category, int count, Action open)
    {
        var tertiary = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = category.Name,
                    Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = count.ToString(ProjectsText.Culture),
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    Foreground = tertiary,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new FontIcon { Glyph = ProjectsGlyphs.ChevronRight, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        _open = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Content = content,
            // Flush with the cards below: the button's own padding would
            // push the title in from their edge.
            Padding = new Thickness(0, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        string name = ProjectsText.Format("Home_CategoryOpen_Format", category.Name);
        AutomationProperties.SetName(_open, name);
        ToolTipService.SetToolTip(_open, name);
        _open.Click += (_, _) => open();

        _editor = new InlineNameEditor
        {
            Text = category.Name,
            TextStyle = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(_editor, ProjectsText.Get("Home_NewCategoryTitleBox"));
        _editor.Committed += text => Renamed?.Invoke(text);
        _editor.Ended += () =>
        {
            _editor.Visibility = Visibility.Collapsed;
            _open.Visibility = Visibility.Visible;
        };

        Content = new Grid { Children = { _open, _editor } };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    // The trimmed title the person kept; the page stores it (an empty one is
    // refused by the store and the title stays).
    public event Action<string>? Renamed;

    public bool IsRenaming => _editor.IsEditing;

    // Turns the title into a text box, selected, as "New category" does.
    public void BeginRename()
    {
        _open.Visibility = Visibility.Collapsed;
        _editor.Visibility = Visibility.Visible;
        _editor.BeginEdit();
        StartBringIntoView();
    }
}
