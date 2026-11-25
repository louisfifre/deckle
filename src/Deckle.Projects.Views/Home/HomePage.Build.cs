using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// Draws Home from the store, and keeps it drawn.
//
// Home is not made again at each change of the store: a section is kept for
// each category and the fold while it has a project, and Build only adds,
// removes and reorders them and hands each its current projects. What the
// person is in the middle of therefore survives a redraw: the scroll offset,
// the keyboard on a card, a category title being typed, and the cards of the
// fold shown past the third row.
internal sealed partial class HomePage
{
    private readonly List<CategorySection> _categorySections = [];
    private WithoutCategoryFold? _fold;

    // The category whose title becomes a text box once the page is drawn: the
    // one "New category" or "Move to a new category..." just made.
    private string? _renameCategoryId;

    private void Build()
    {
        ProjectStore store = _context.Store;

        // Nothing at all: the empty screen (H0).
        bool empty = store.HoldsNothing();
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        NewCategoryButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        AddButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (empty)
        {
            Sections.Children.Clear();
            _categorySections.Clear();
            _fold = null;
            return;
        }

        // The sections in the order the store keeps the categories, then the
        // fold: a kept one is updated, a new one is made.
        List<CategorySection> sections = [];
        foreach (ProjectCategory category in store.Categories)
        {
            IReadOnlyList<ProjectEntry> projects = store.ProjectsIn(category.Id);
            CategorySection? section = _categorySections.FirstOrDefault(s => s.CategoryId == category.Id);
            if (section is null)
            {
                section = new CategorySection(_context, this, category, projects);
                section.Cards.ShownChanged += OnShownChanged;
            }
            else
            {
                section.Update(category, projects);
            }
            sections.Add(section);
        }

        IReadOnlyList<ProjectEntry> loose = store.ProjectsIn(null);
        if (loose.Count == 0)
        {
            _fold = null;
        }
        else if (_fold is null)
        {
            _fold = new WithoutCategoryFold(_context, this, loose);
            _fold.Cards.ShownChanged += OnShownChanged;
        }
        else
        {
            _fold.Update(loose);
        }
        // Without a category on Home, the fold would hide the only projects
        // there are behind a closed header: they show as the page's grid.
        if (_fold is not null) _fold.Folds = HomeLayout.FoldsLooseProjects(store.Categories.Count);

        _categorySections.Clear();
        _categorySections.AddRange(sections);

        // Put the page's children in that order, touching only what is not
        // already in place.
        List<UIElement> wanted = [.. sections];
        if (_fold is not null) wanted.Add(_fold);
        for (int i = Sections.Children.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Sections.Children[i])) Sections.Children.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = Sections.Children.IndexOf(wanted[i]);
            if (at == i) continue;
            if (at > i) Sections.Children.RemoveAt(at);
            Sections.Children.Insert(i, wanted[i]);
        }

        if (_renameCategoryId is { } renaming)
        {
            _renameCategoryId = null;
            _categorySections.FirstOrDefault(s => s.CategoryId == renaming)?.Header.BeginRename();
        }
    }
}
