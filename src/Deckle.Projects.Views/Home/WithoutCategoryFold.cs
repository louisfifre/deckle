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
// The header is a category's header (SectionHeaderLabel) on the native
// Expander, whose chevron closes and opens the fold and whose automation peer
// gives Narrator and Voice Access the expand and collapse pattern. The Expander
// is a flat line here, not a card: its chrome is turned off through the
// lightweight styling resources the control reads (Expander.Resources below).
// It holds the header only. The cards sit under it, because an Expander that
// hugs its header (so the chevron follows the count, as a category's chevron
// does) would give its content no more width than the content asks for, and the
// grid needs the whole width to plan its columns.
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
    private readonly Expander _expander;
    private readonly SectionHeaderLabel _label = new();
    private readonly StackPanel _body;

    public WithoutCategoryFold(ProjectsContext context, IProjectCardCommands commands, IReadOnlyList<ProjectEntry> projects)
    {
        _context = context;

        _expander = new Expander
        {
            Header = _label,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            // The height of a category's header button: a 28 line of subtitle
            // text and 4 above and below.
            MinHeight = 36,
            // The template reads the header's 16 DIP left padding with
            // StaticResource, which an override on the control does not reach
            // (seen in the self-check capture: the title stood 16 in from the
            // cards). The margin takes that padding back.
            Margin = new Thickness(-16, 0, 0, 0),
            Resources = Flat(),
        };
        _expander.Expanding += (_, _) => IsOpen = true;
        _expander.Collapsed += (_, _) => IsOpen = false;

        Cards = new ProjectGrid(context, commands);

        TextBlock hint = Themed.Text("TextFillColorSecondaryBrush", "CaptionTextBlockStyle");
        hint.Text = ProjectsText.Get("Home_WithoutCategoryHint");
        hint.VerticalAlignment = VerticalAlignment.Center;
        _body = new StackPanel { Spacing = 12, Children = { hint, Cards } };

        var panel = new StackPanel
        {
            Spacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _expander, _body },
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

    // The Expander without its card: no fill, no border, no padding, and the
    // chevron right after the header as a category's chevron is. These are the
    // control's own lightweight styling resources (generic.xaml, Expander), so
    // they hold in every theme, high contrast included.
    private static ResourceDictionary Flat()
    {
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var none = new Thickness(0);
        return new ResourceDictionary
        {
            ["ExpanderBackground"] = clear,
            ["ExpanderBorderBrush"] = clear,
            ["ExpanderBorderThickness"] = none,
            ["ExpanderHeaderBackground"] = clear,
            ["ExpanderHeaderBorderBrush"] = clear,
            ["ExpanderHeaderBorderPointerOverBrush"] = clear,
            ["ExpanderHeaderBorderPressedBrush"] = clear,
            ["ExpanderHeaderBorderThickness"] = none,
            ["ExpanderContentBackground"] = clear,
            ["ExpanderContentBorderBrush"] = clear,
            ["ExpanderContentDownBorderThickness"] = none,
            ["ExpanderContentUpBorderThickness"] = none,
            ["ExpanderContentPadding"] = none,
            ["ExpanderChevronMargin"] = new Thickness(8, 0, 0, 0),
        };
    }

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

    // The projects in no category as the store has them now.
    public void Update(IReadOnlyList<ProjectEntry> projects)
    {
        _label.Set(ProjectsText.Get("Home_WithoutCategory"), projects.Count);
        AutomationProperties.SetName(_expander, ProjectsText.Format("Home_WithoutCategoryToggle_Format", projects.Count));
        Cards.SetProjects(projects, ProjectsText.Get("Home_WithoutCategory"), Cards.ShowAll);
        Apply();
    }

    private void Apply()
    {
        _expander.IsExpanded = IsOpen;
        _body.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }
}
