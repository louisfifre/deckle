using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// Draws Home from the store.
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

        // A redraw must not take a title away from the person typing it.
        foreach (CategorySection open in _categorySections)
            if (open.Header.IsRenaming) _renameCategoryId = open.CategoryId;

        // Nothing at all: the empty screen (H0).
        bool empty = store.HoldsNothing();
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        NewCategoryButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        AddButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        Sections.Children.Clear();
        _categorySections.Clear();
        _fold = null;
        if (empty) return;

        foreach (ProjectCategory category in store.Categories)
        {
            var section = new CategorySection(_context, this, category, store.ProjectsIn(category.Id));
            section.Cards.ShownChanged += OnShownChanged;
            _categorySections.Add(section);
            Sections.Children.Add(section);
        }

        IReadOnlyList<ProjectEntry> loose = store.ProjectsIn(null);
        if (loose.Count > 0)
        {
            _fold = new WithoutCategoryFold(_context, this, loose);
            _fold.Cards.ShownChanged += OnShownChanged;
            Sections.Children.Add(_fold);
        }

        if (_renameCategoryId is { } renaming)
        {
            _renameCategoryId = null;
            _categorySections.FirstOrDefault(s => s.CategoryId == renaming)?.Header.BeginRename();
        }
    }
}
