using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// Draws the page from the store: the title, the count line, and one row per
// project in the order the store keeps (the order Home shows).
internal sealed partial class CategoryPage
{
    private readonly HashSet<string> _drawn = [];

    // True while Build fills the list, so the list's own change notification
    // is not taken for the person reordering it (CategoryPage.Reorder.cs).
    private bool _building;

    // Draws the page and returns the projects that were not drawn before, which
    // are the ones whose readings are worth reading: a reorder or a rename draws
    // the same projects again and costs no git process.
    private IReadOnlyList<ProjectEntry> Build()
    {
        // The category was deleted: the window is about to leave this page, and
        // there is nothing left to draw.
        if (_context.Store.FindCategory(_categoryId) is not { } category) return [];

        // A redraw must not take the title away from the person typing it.
        if (!TitleEditor.IsEditing) TitleEditor.Text = category.Name;

        IReadOnlyList<ProjectEntry> projects = ProjectsHere();
        CountText.Text = projects.Count switch
        {
            0 => ProjectsText.Get("Category_CountNone"),
            1 => ProjectsText.Get("Category_CountOne"),
            _ => ProjectsText.Format("Category_Count_Format", projects.Count),
        };

        // An empty category says in one sentence to drag a project onto it, as
        // it does on Home, under the heading and above the delete button (they
        // are the list's header and footer, so the empty list stays in the tree).
        EmptyText.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        List<ProjectEntry> fresh = [.. projects.Where(p => !_drawn.Contains(p.Id))];
        _drawn.Clear();
        foreach (ProjectEntry project in projects) _drawn.Add(project.Id);

        _building = true;
        try
        {
            Rows.Items.Clear();
            foreach (ProjectEntry project in projects) Rows.Items.Add(new CategoryRow(_context, project));
        }
        finally
        {
            _building = false;
        }
        return fresh;
    }

    private IEnumerable<CategoryRow> RowControls() => Rows.Items.OfType<CategoryRow>();

    private CategoryRow? RowOf(string projectId) => RowControls().FirstOrDefault(r => r.ProjectId == projectId);

    private void RefreshAllRows()
    {
        foreach (CategoryRow row in RowControls()) row.Refresh();
    }

    // A click on a row outside its button opens the project's page. The list
    // does not raise it for a click on the button, which handles its own.
    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CategoryRow row) _context.Navigate(new ProjectsTarget.Project(row.ProjectId));
    }
}
