using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// Home: the title row, the categories in the order they were created, then the
// "Without a category" fold (boards H0 to H3). This file holds the page's
// lifetime and what keeps it up to date; HomePage.Build.cs draws it from the
// store, HomePage.Commands.cs holds what its buttons and its cards' menus do.
//
// The page listens to three things, each marshalled to the window's thread
// because the engine raises them wherever the work finished:
//
//   the store          a project or a category was added, moved, renamed or
//                      removed: the page is drawn again, once for any number of
//                      changes that arrive together
//   the readings       one project's version or uncommitted count changed: only
//                      its card is redrawn
//   the board          an action began or ended: only that project's card is
//                      redrawn (its ring, its button, its warning mark)
//
// Dispose ends all three, so a page the person left costs nothing.
internal sealed partial class HomePage : Page, IProjectsPage
{
    private readonly ProjectsContext _context;
    private readonly CancellationTokenSource _lifetime = new();

    // The projects read during this visit, so the first look at a project's
    // card reads it and a change of width reads only the cards that appeared.
    private readonly HashSet<string> _read = [];

    private int _rebuildQueued;
    private bool _disposed;

    public HomePage(ProjectsContext context)
    {
        _context = context;
        InitializeComponent();

        TitleText.Text = ProjectsText.Get("Home_Title");
        NewCategoryLabel.Text = ProjectsText.Get("Home_NewCategory");
        AddLabel.Text = ProjectsText.Get("Home_AddProject");
        EmptyState.AddRequested += () => _ = AddProjectAsync();

        _context.Store.Changed += OnStoreChanged;
        _context.Workspace.Readings.ReadingChanged += OnReadingChanged;
        _context.Workspace.Board.Changed += OnBoardChanged;

        // The window never receives a page whose constructor threw, so it cannot
        // dispose it: the page lets go of what it subscribed to.
        try
        {
            Build();
        }
        catch (Exception)
        {
            Dispose();
            throw;
        }
    }

    // F5 and the window coming back to the front: every card on screen is read
    // again, and the page returns once the new values are drawn.
    public async Task RefreshAsync(RefreshReason reason)
    {
        IReadOnlyList<ProjectEntry> shown = AllShown();
        foreach (ProjectEntry project in shown) _read.Add(project.Id);
        await ReadAsync(shown);
        if (!_disposed) RefreshAllCards();
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

    // A grid gave the cards it draws. Those not read yet during this visit are
    // read now (cards beyond the three rows are not drawn, so not read).
    private void OnShownChanged(IReadOnlyList<ProjectEntry> shown)
    {
        List<ProjectEntry> fresh = [.. shown.Where(p => _read.Add(p.Id))];
        if (fresh.Count > 0) _ = ReadAsync(fresh);
    }

    private async Task ReadAsync(IReadOnlyList<ProjectEntry> projects)
    {
        if (projects.Count == 0 || _lifetime.IsCancellationRequested) return;
        try
        {
            await _context.RefreshReadingsAsync(projects, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // The person left Home: the previous readings stand.
        }
    }

    private void OnStoreChanged()
    {
        // Many changes in a row (a category made, a project moved into it) are
        // one redraw. The flag is taken with Interlocked because the store may
        // raise on any thread.
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 1) return;
        _context.Dispatch(() =>
        {
            Interlocked.Exchange(ref _rebuildQueued, 0);
            if (!_disposed) Build();
        });
    }

    private void OnReadingChanged(string projectId) =>
        _context.Dispatch(() =>
        {
            if (!_disposed) RefreshCard(projectId);
        });

    private void OnBoardChanged(string projectId) =>
        _context.Dispatch(() =>
        {
            if (!_disposed) RefreshCard(projectId);
        });

    private void RefreshCard(string projectId)
    {
        foreach (CategorySection section in _categorySections) section.Cards.Refresh(projectId);
        _fold?.Cards.Refresh(projectId);
    }

    private void RefreshAllCards()
    {
        foreach (CategorySection section in _categorySections) section.Cards.RefreshAll();
        _fold?.Cards.RefreshAll();
    }

    // Every project whose card is drawn now.
    private IReadOnlyList<ProjectEntry> AllShown()
    {
        List<ProjectEntry> shown = [.. _categorySections.SelectMany(s => s.Cards.Shown)];
        if (_fold is not null) shown.AddRange(_fold.Cards.Shown);
        return shown;
    }
}
