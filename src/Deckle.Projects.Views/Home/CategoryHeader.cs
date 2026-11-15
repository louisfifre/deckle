using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// The line above a category's cards on Home (board H1): its title, the number
// of its projects, and a chevron. Clicking it opens the category's page (spec:
// "clicking the category's title opens its page").
//
// A standard subtle button holds the three, so the keyboard reaches it, Enter
// and Space press it and a screen reader reads it as a link to the page. Right
// after "New category" the title is a text box to type in instead
// (BeginRename), and the button comes back once the box closes.
//
// The header is made once for a category and kept while the page lives: Update
// brings it to the store's current title and count, and a title being typed is
// not touched by it.
internal sealed class CategoryHeader : UserControl
{
    private readonly Button _open;
    private readonly TextBlock _title;
    private readonly TextBlock _count;
    private readonly InlineNameEditor _editor;
    private string _name;

    public CategoryHeader(ProjectCategory category, int count, Action open)
    {
        _name = category.Name;
        _title = new TextBlock
        {
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        _count = Themed.Text("TextFillColorTertiaryBrush", "BodyTextBlockStyle");
        _count.VerticalAlignment = VerticalAlignment.Center;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _title,
                _count,
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
        _open.Click += (_, _) => open();

        _editor = new InlineNameEditor
        {
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
        Update(category.Name, count);
    }

    // The trimmed title the person kept; the page stores it (an empty one is
    // refused by the store and the title stays).
    public event Action<string>? Renamed;

    public bool IsRenaming => _editor.IsEditing;

    // The title and the count as the store has them now. The box keeps what
    // the person is typing; it takes the title again when it next opens.
    public void Update(string name, int count)
    {
        _name = name;
        _title.Text = name;
        _count.Text = count.ToString(ProjectsText.Culture);
        if (!_editor.IsEditing) _editor.Text = name;

        string label = ProjectsText.Format("Home_CategoryOpen_Format", name);
        AutomationProperties.SetName(_open, label);
        ToolTipService.SetToolTip(_open, label);
    }

    // Turns the title into a text box, selected, as "New category" does.
    public void BeginRename()
    {
        _open.Visibility = Visibility.Collapsed;
        _editor.Visibility = Visibility.Visible;
        _editor.Text = _name;
        _editor.BeginEdit();
        StartBringIntoView();
    }
}
