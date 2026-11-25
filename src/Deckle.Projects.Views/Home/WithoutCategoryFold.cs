using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// The fold at the bottom of Home that holds the projects in no category (boards
// H1 and H3): a header with its name and the number of its projects, closed by
// default; opened, the sentence "Drag a card onto a category, or use its menu."
// and the cards in the same grid, with the same cap.
//
// The header is a category's header: the same subtle button holding the same
// SectionHeaderLabel, with the same padding, so both groups have one form and
// one gap to what follows. Only the chevron differs, by direction, as Windows
// does it: a category's ">" opens its page, this one's down/up opens and closes
// the fold. Narrator reads the state through ItemStatus.
//
// Not the native Expander: an expanded Expander keeps a content area under its
// header even when it holds nothing, and no lightweight resource removes it
// (seen in the self-check capture, the gap to the hint about twice a category's
// gap to its cards); the cards cannot go inside it either, since an Expander
// that hugs its header gives its content no more width than the content asks
// for, and the grid needs the whole width to plan its columns.
//
// The page only builds it when it has a project: with none, the fold is not
// shown (spec: not drawn in v4). Like a category, the whole fold is a drop
// target, so a card let go anywhere over it leaves its category.
//
// The fold is made once and kept while it has a project: Update gives it the
// projects as the store has them now, and its grid keeps what the person did
// there (the cards past the third row shown, a card with the keyboard on it).
internal sealed class WithoutCategoryFold : UserControl
{
    private readonly ProjectsContext _context;
    private readonly Button _toggle;
    private readonly FontIcon _chevron;
    private readonly SectionHeaderLabel _label = new();
    private readonly TextBlock _hint;
    private readonly StackPanel _body;

    // False while Home has no category (HomeLayout): the header and the hint
    // go, and the cards always show.
    private bool _folds = true;

    public WithoutCategoryFold(ProjectsContext context, IProjectCardCommands commands, IReadOnlyList<ProjectEntry> projects)
    {
        _context = context;

        _chevron = new FontIcon { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _toggle = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _label, _chevron },
            },
            // As CategoryHeader: flush with the cards below.
            Padding = new Thickness(0, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // The person's click is the one writer of the open state; Update only
        // re-applies it, so a store change cannot undo a click.
        _toggle.Click += (_, _) => IsOpen = !IsOpen;

        Cards = new ProjectGrid(context, commands);

        _hint = Themed.Text("TextFillColorSecondaryBrush", "CaptionTextBlockStyle");
        _hint.Text = ProjectsText.Get("Home_WithoutCategoryHint");
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _body = new StackPanel { Spacing = 12, Children = { _hint, Cards } };

        var panel = new StackPanel
        {
            Spacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _toggle, _body },
        };
        FrameworkElement dropTarget = CategoryDropTarget.Attach(panel, () => ProjectsText.Get("Home_WithoutCategory"), projectId => context.Store.Find(projectId)?.CategoryId is null, projectId =>
        {
            if (context.Store.Find(projectId)?.CategoryId is not null) commands.Move(projectId, null);
        });

        Content = dropTarget;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Update(projects);
    }

    public ProjectGrid Cards { get; }

    // Open or closed, kept by the window so the fold is as the person left it
    // when Home is shown again.
    public bool IsOpen
    {
        get => _context.WithoutCategoryOpen;
        set
        {
            _context.WithoutCategoryOpen = value;
            Apply();
        }
    }

    // Whether the projects sit in the fold or are Home's own grid: the page
    // says it from the number of categories (HomeLayout).
    public bool Folds
    {
        get => _folds;
        set
        {
            _folds = value;
            Apply();
        }
    }

    // The projects in no category as the store has them now.
    public void Update(IReadOnlyList<ProjectEntry> projects)
    {
        _label.Set(ProjectsText.Get("Home_WithoutCategory"), projects.Count);
        AutomationProperties.SetName(_toggle, ProjectsText.Format("Home_WithoutCategoryToggle_Format", projects.Count));
        Cards.SetProjects(projects, ProjectsText.Get("Home_WithoutCategory"), Cards.ShowAll);
        Apply();
    }

    private void Apply()
    {
        _toggle.Visibility = _folds ? Visibility.Visible : Visibility.Collapsed;
        _hint.Visibility = _folds ? Visibility.Visible : Visibility.Collapsed;
        if (!_folds)
        {
            _body.Visibility = Visibility.Visible;
            return;
        }

        bool open = IsOpen;
        _chevron.Glyph = open ? ProjectsGlyphs.ChevronUp : ProjectsGlyphs.ChevronDown;
        AutomationProperties.SetItemStatus(_toggle, ProjectsText.Get(open ? "Home_WithoutCategory_Expanded" : "Home_WithoutCategory_Collapsed"));
        _body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }
}
