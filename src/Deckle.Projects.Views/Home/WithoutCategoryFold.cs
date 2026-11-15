using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// The fold at the bottom of Home that holds the projects in no category (boards
// H1 and H3): a line with a chevron, its name and the number of its projects,
// closed by default; opened, the sentence "Drag a card onto a category, or use
// its menu." and the cards in the same grid, with the same cap.
//
// The page only builds it when it has a project: with none, the fold is not
// shown (spec: not drawn in v4). Like a category, the whole fold is a drop
// target, so a card let go anywhere over it leaves its category.
internal sealed class WithoutCategoryFold : UserControl
{
    private readonly ProjectsContext _context;
    private readonly FontIcon _chevron;
    private readonly StackPanel _body;

    public WithoutCategoryFold(ProjectsContext context, IProjectCardCommands commands, IReadOnlyList<ProjectEntry> projects)
    {
        _context = context;

        _chevron = new FontIcon { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(0, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    _chevron,
                    new TextBlock
                    {
                        Text = ProjectsText.Get("Home_WithoutCategory"),
                        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = projects.Count.ToString(ProjectsText.Culture),
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                        Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(toggle, ProjectsText.Format("Home_WithoutCategoryToggle_Format", projects.Count));
        toggle.Click += (_, _) => IsOpen = !IsOpen;

        Cards = new ProjectGrid(context, commands);
        Cards.SetProjects(projects, ProjectsText.Get("Home_WithoutCategory"), Cards.ShowAll);

        _body = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = ProjectsText.Get("Home_WithoutCategoryHint"),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
                Cards,
            },
        };

        var panel = new StackPanel
        {
            Spacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { toggle, _body },
        };
        CategoryDropTarget.Attach(panel, ProjectsText.Get("Home_WithoutCategory"), projectId =>
        {
            if (context.Store.Find(projectId)?.CategoryId is not null) commands.Move(projectId, null);
        });

        Content = panel;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Apply();
    }

    public ProjectGrid Cards { get; }

    // Chevron down when closed, up when open, as Windows' own folds draw it.
    public bool IsOpen
    {
        get => _context.WithoutCategoryOpen;
        set
        {
            _context.WithoutCategoryOpen = value;
            Apply();
        }
    }

    private void Apply()
    {
        _chevron.Glyph = IsOpen ? ProjectsGlyphs.ChevronUp : ProjectsGlyphs.ChevronDown;
        _body.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }
}
