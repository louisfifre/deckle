using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// A category's page (board C1): its title (renamed in place), the number of its
// projects, one row per project in the order Home shows them, and the button
// that deletes the category. This file holds the page's lifetime and what keeps it
// up to date; CategoryPage.Rows.cs draws the list, CategoryPage.Reorder.cs
// holds the drag, CategoryPage.Delete.cs the delete button and its flyout.
//
// Like Home, the page listens to three things, each marshalled to the window's
// thread because the engine raises them wherever the work finished:
//
//   the store          a project or the category changed: the page is drawn
//                      again, once for any number of changes that arrive together
//   the readings       one project's version or uncommitted count changed: only
//                      its row is redrawn
//   the board          an action began or ended: only that project's row is
//                      redrawn (its ring, its button, its warning mark)
//
// Dispose ends all three, so a page the person left costs nothing.
internal sealed partial class CategoryPage : Page, IProjectsPage
{
    private readonly ProjectsContext _context;
    private readonly string _categoryId;
    private readonly CancellationTokenSource _lifetime = new();

    private int _rebuildQueued;
    private bool _disposed;

    public CategoryPage(ProjectsContext context, string categoryId)
    {
        _context = context;
        _categoryId = categoryId;
        InitializeComponent();

        BreadcrumbHost.Content = Breadcrumb.ForCategoryPage(_context);

        TitleEditor.TextStyle = (Style)Application.Current.Resources["TitleTextBlockStyle"];
        AutomationProperties.SetName(TitleEditor, ProjectsText.Get("Category_Rename"));
        TitleEditor.Committed += OnTitleCommitted;

        DeleteLabel.Text = ProjectsText.Get("Category_Delete");
        DeleteFlyout.Opening += OnDeleteFlyoutOpening;
        DeleteConfirmButton.Content = ProjectsText.Get("Category_DeleteConfirm");
        EmptyText.Text = ProjectsText.Get("Category_Empty");

        _context.Store.Changed += OnStoreChanged;
        _context.Workspace.Readings.ReadingChanged += OnReadingChanged;
        _context.Workspace.Board.Changed += OnBoardChanged;

        // The window never receives a page whose constructor threw, so it cannot
        // dispose it: the page lets go of what it subscribed to.
        try
        {
            Wire();
            _ = ReadAsync(Build());
        }
        catch (Exception)
        {
            Dispose();
            throw;
        }
    }

    // F5 and the window coming back to the front: every row is read again, and
    // the page returns once the new values are drawn.
    public async Task RefreshAsync(RefreshReason reason)
    {
        await ReadAsync(ProjectsHere());
        if (!_disposed) RefreshAllRows();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.Store.Changed -= OnStoreChanged;
        _context.Workspace.Readings.ReadingChanged -= OnReadingChanged;
        _context.Workspace.Board.Changed -= OnBoardChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private IReadOnlyList<ProjectEntry> ProjectsHere() => _context.Store.ProjectsIn(_categoryId);

    private async Task ReadAsync(IReadOnlyList<ProjectEntry> projects)
    {
        if (projects.Count == 0 || _lifetime.IsCancellationRequested) return;
        try
        {
            await _context.RefreshReadingsAsync(projects, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // The person left the page: the previous readings stand.
        }
    }

    // The title the person kept. The store refuses an empty one and the title
    // stays what it was (spec: a category's title is renamed the way a
    // project's is; there is no folder name to fall back to here).
    private void OnTitleCommitted(string text)
    {
        _context.Store.RenameCategory(_categoryId, text);
        if (_context.Store.FindCategory(_categoryId) is { } category) TitleEditor.Text = category.Name;
    }

    private void OnStoreChanged()
    {
        // Many changes in a row are one redraw. The flag is taken with
        // Interlocked because the store may raise on any thread.
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 1) return;
        _context.Dispatch(() =>
        {
            Interlocked.Exchange(ref _rebuildQueued, 0);
            if (_disposed) return;
            _ = ReadAsync(Build());
        });
    }

    private void OnReadingChanged(string projectId) =>
        _context.Dispatch(() =>
        {
            if (!_disposed) RowOf(projectId)?.Refresh();
        });

    private void OnBoardChanged(string projectId) =>
        _context.Dispatch(() =>
        {
            if (!_disposed) RowOf(projectId)?.Refresh();
        });
}
