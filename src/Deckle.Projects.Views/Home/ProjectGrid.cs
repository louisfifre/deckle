using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

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
// The grid is kept, and so are its cards: a change of projects or of width
// adds, removes or reorders items of one observable list the repeater listens
// to, so a card that stays keeps its keyboard focus, its hover and the anchor
// of an open menu. An element is handed to the repeater only once, when it is
// new: the repeater parents what it is given, and an element that still has a
// parent from before must not be handed to it again. So a card that leaves the
// list is dropped, and one that comes back (a reorder, a project that left and
// returned) is made again. Twelve cards at most are made, so nothing is saved
// by recycling them. The page asks Refresh(projectId) to redraw one card when
// its reading or its action changes.
//
// The plan is made on the first measure, from the width the parent offers, so
// a new grid draws its cards in its first frame instead of waiting for a size
// change. Changing the items there is safe: the repeater is this control's own
// child and is measured right after, in the same pass. What must not happen
// inside a layout pass is the outside world reacting, so ShownChanged, raised
// from a measure, is queued on the dispatcher and arrives after the pass. A
// SizeChanged or a store change plans outside layout and raises it at once.
internal sealed class ProjectGrid : UserControl
{
    private readonly ProjectsContext _context;
    private readonly IProjectCardCommands _commands;
    private readonly ItemsRepeater _repeater;

    // What the repeater is given, and for each item the key it was made for: a
    // project's id, or "more:<n>:<category>" for the tile.
    private readonly ObservableCollection<UIElement> _items = [];
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, ProjectCard> _cards = [];

    private IReadOnlyList<ProjectEntry> _projects = [];
    private string _categoryName = "";
    private Action? _openCategory;
    private CardGridPlan? _plan;
    private double _width;
    private bool _showAll;
    private List<string> _shownIds = [];

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
            ItemsSource = _items,
        };
        Content = _repeater;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SizeChanged += (_, e) => Replan(e.NewSize.Width, force: false);
    }

    // Raised when the set of projects drawn changes (the width gave more places
    // or fewer, or the projects changed), with the projects now drawn, so the
    // page can read the ones that were not on screen before.
    public event Action<IReadOnlyList<ProjectEntry>>? ShownChanged;

    public IReadOnlyList<ProjectEntry> Shown => _plan is null ? [] : [.. _projects.Take(_plan.CardsShown)];

    // The tile, when the plan has one, for the self-check.
    public bool ShowsMoreTile => _plan?.ShowsMoreTile ?? false;

    public int ColumnsForReview => _plan?.Columns ?? 0;

    // The projects of the section in the order the store keeps them. openCategory
    // is what the "+ N more" tile does; with none, the tile is not offered (the
    // fold has no page to open, but it caps its cards all the same). Called
    // again at every change of the store: what is already drawn stays, and the
    // "show all" choice of the fold is kept.
    public void SetProjects(IReadOnlyList<ProjectEntry> projects, string categoryName, Action? openCategory)
    {
        _projects = projects;
        _categoryName = categoryName;
        _openCategory = openCategory;
        Replan(_width, force: true);
    }

    // Lifts the cap: every project is drawn and the tile goes. The fold of
    // projects without a category has no page for its tile to open, so the tile
    // opens the rest in place (a choice of this build; the spec gives the fold
    // the same grid with the same cap).
    public void ShowAll()
    {
        _showAll = true;
        Replan(_width, force: true);
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

    protected override Size MeasureOverride(Size availableSize)
    {
        // The width the parent offers is the width the grid will have (it
        // stretches), so the cards are made now and measured in this same pass.
        if (!double.IsInfinity(availableSize.Width)) Replan(availableSize.Width, force: false, inMeasure: true);
        return base.MeasureOverride(availableSize);
    }

    private void Replan(double width, bool force, bool inMeasure = false)
    {
        // Before the first layout the width is not known; planning with zero
        // would draw one column's worth of cards and then change.
        if (width <= 0 || double.IsNaN(width)) return;
        _width = width;

        CardGridPlan plan = CardGridCapacity.Plan(width, _projects.Count);
        if (_showAll) plan = plan with { CardsShown = _projects.Count, MoreCount = 0 };
        if (plan == _plan && !force) return;
        _plan = plan;

        List<string> keys = [.. _projects.Take(plan.CardsShown).Select(p => p.Id)];
        if (plan.ShowsMoreTile && _openCategory is not null) keys.Add($"more:{plan.MoreCount}:{_categoryName}");

        // First what no longer belongs, then each place in order: an item in
        // the right place stays, one in the wrong place is made again.
        for (int i = _keys.Count - 1; i >= 0; i--)
            if (!keys.Contains(_keys[i])) RemoveAt(i);
        for (int i = 0; i < keys.Count; i++)
        {
            if (i < _keys.Count && _keys[i] == keys[i]) continue;
            int elsewhere = _keys.IndexOf(keys[i]);
            if (elsewhere > i) RemoveAt(elsewhere);
            Insert(i, keys[i], plan);
        }
        while (_keys.Count > keys.Count) RemoveAt(_keys.Count - 1);

        // A card that stayed shows the project as the store has it now.
        RefreshAll();

        List<string> shownIds = [.. _projects.Take(plan.CardsShown).Select(p => p.Id)];
        if (!shownIds.SequenceEqual(_shownIds))
        {
            _shownIds = shownIds;
            // From a measure pass the observers (the page reads projects and may
            // redraw) are told afterwards, never inside the layout pass.
            if (inMeasure) DispatcherQueue.TryEnqueue(() => ShownChanged?.Invoke(Shown));
            else ShownChanged?.Invoke(Shown);
        }
    }

    private void RemoveAt(int index)
    {
        _cards.Remove(_keys[index]);
        _keys.RemoveAt(index);
        _items.RemoveAt(index);
    }

    private void Insert(int index, string key, CardGridPlan plan)
    {
        UIElement element;
        if (_projects.FirstOrDefault(p => p.Id == key) is { } project)
        {
            var card = new ProjectCard(_context, _commands, project);
            _cards[key] = card;
            element = card;
        }
        else
        {
            element = new MoreTile(plan, _categoryName, _openCategory!);
        }
        _keys.Insert(index, key);
        _items.Insert(index, element);
    }
}
