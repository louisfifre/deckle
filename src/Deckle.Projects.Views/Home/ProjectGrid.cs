using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// The cards of one category (or of the "Without a category" fold) in the native
// adaptive grid: an ItemsRepeater with a UniformGridLayout, which puts as many
// cards across as the width allows, each at least 240 DIP wide with 12 between.
//
// The layout cannot cap the rows, so the count of cards is decided here by
// CardGridCapacity from the width the grid is given, and the layout is handed
// exactly that many items: at most three rows, the last place a "+ N more" tile
// when the category holds more (spec, Home). CardGridCapacity and the layout
// read the same minimum width and gap, so they always agree on the columns.
//
// A card or a tile is made again whenever the plan changes, not kept: the
// repeater would otherwise be handed an element that still has a parent from the
// plan before. Twelve cards at most are made, so nothing is saved by keeping
// them. The page asks Refresh(projectId) to redraw one card when its reading
// or its action changes.
internal sealed class ProjectGrid : UserControl
{
    private readonly ProjectsContext _context;
    private readonly IProjectCardCommands _commands;
    private readonly ItemsRepeater _repeater;
    private readonly Dictionary<string, ProjectCard> _cards = [];

    private IReadOnlyList<ProjectEntry> _projects = [];
    private string _categoryName = "";
    private Action? _openCategory;
    private CardGridPlan? _plan;
    private bool _showAll;

    public ProjectGrid(ProjectsContext context, IProjectCardCommands commands)
    {
        _context = context;
        _commands = commands;
        _repeater = new ItemsRepeater
        {
            Layout = new UniformGridLayout
            {
                Orientation = Orientation.Horizontal,
                MinItemWidth = CardGridCapacity.MinCardWidth,
                MinItemHeight = (double)Application.Current.Resources["ProjectsCardMinHeight"],
                MinColumnSpacing = CardGridCapacity.Gap,
                MinRowSpacing = CardGridCapacity.Gap,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill,
            },
        };
        Content = _repeater;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SizeChanged += (_, _) => Replan();
    }

    // Raised when the set of projects drawn changes (the width gave more places
    // or fewer), with the projects now drawn, so the page can read the ones
    // that were not on screen before.
    public event Action<IReadOnlyList<ProjectEntry>>? ShownChanged;

    public IReadOnlyList<ProjectEntry> Shown => _plan is null ? [] : [.. _projects.Take(_plan.CardsShown)];

    // The tile, when the plan has one, for the self-check.
    public bool ShowsMoreTile => _plan?.ShowsMoreTile ?? false;

    public int ColumnsForReview => _plan?.Columns ?? 0;

    // The projects of the section in the order the store keeps them. openCategory
    // is what the "+ N more" tile does; with none, the tile is not offered (the
    // fold has no page to open, but it caps its cards all the same).
    public void SetProjects(IReadOnlyList<ProjectEntry> projects, string categoryName, Action? openCategory)
    {
        _projects = projects;
        _categoryName = categoryName;
        _openCategory = openCategory;
        _plan = null;
        _showAll = false;
        Replan();
    }

    // Lifts the cap: every project is drawn and the tile goes. The fold of
    // projects without a category has no page for its tile to open, so the tile
    // opens the rest in place (a choice of this build; the spec gives the fold
    // the same grid with the same cap).
    public void ShowAll()
    {
        _showAll = true;
        _plan = null;
        Replan();
    }

    public void Refresh(string projectId)
    {
        if (_cards.TryGetValue(projectId, out ProjectCard? card)) card.Refresh();
    }

    public void RefreshAll()
    {
        foreach (ProjectCard card in _cards.Values) card.Refresh();
    }

    public ProjectCard? CardOf(string projectId) => _cards.GetValueOrDefault(projectId);

    private void Replan()
    {
        // Before the first layout the width is not known; planning with zero
        // would draw one column's worth of cards and then change.
        if (ActualWidth <= 0) return;

        CardGridPlan plan = CardGridCapacity.Plan(ActualWidth, _projects.Count);
        if (_showAll) plan = plan with { CardsShown = _projects.Count, MoreCount = 0 };
        if (plan == _plan) return;
        _plan = plan;

        var items = new List<UIElement>();
        _cards.Clear();
        foreach (ProjectEntry project in _projects.Take(plan.CardsShown))
        {
            var card = new ProjectCard(_context, _commands, project);
            _cards[project.Id] = card;
            items.Add(card);
        }
        if (plan.ShowsMoreTile && _openCategory is { } open) items.Add(new MoreTile(plan, _categoryName, open));

        _repeater.ItemsSource = items;
        ShownChanged?.Invoke(Shown);
    }
}
