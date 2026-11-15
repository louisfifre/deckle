using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// A project's page (boards P1 to P4): a hub of links and plain readouts, and the
// few actions the person pinned. It reads the folder and writes nothing about
// it; what it keeps of the project is the store's (name, category, pins).
//
// This file holds the page's lifetime and what keeps it up to date. The
// partials beside it draw one area each:
//
//   ProjectPage.Header.cs     breadcrumb, name and rename, path, the script menu
//                             and the main action, the main action's last result
//   ProjectPage.Readouts.cs   version, uncommitted files, what an agent reads
//   ProjectPage.OpenIn.cs     the row of links
//   ProjectPage.Actions.cs    the pinned actions and what their buttons start
//   ProjectPage.Missing.cs    a project whose folder is gone
//   ProjectPage.Timer.cs      the one-second tick of "Running · 1 min 12 s"
//   ProjectPage.Review.cs     what the self-check drives
//
// The page listens to three things, each marshalled to the window's thread
// because the engine raises them wherever the work finished:
//
//   the store      the name, the category or the pins changed
//   the readings   this project was read again
//   the board      an action of this project began or ended
//
// and Dispose ends all three, the tick and any reading in flight, so a page the
// person left costs nothing. Leaving the page never stops an action: it lives in
// its terminal (spec, Behaviours).
internal sealed partial class ProjectPage : Page, IProjectsPage
{
    private readonly ProjectsContext _context;
    private readonly string _projectId;
    private readonly CancellationTokenSource _lifetime = new();

    // What the script menu answered the last time it was read, or null before
    // the first answer. Only the menu itself says what it gives; the pins the
    // store keeps are a copy of its labels.
    private ScriptMenuListing? _listing;

    // The estimate behind "An agent always reads": read once per opening of the
    // page, and again on Refresh. Null while it has not been read, and after a
    // read that failed (_agentRead tells the two apart).
    private AgentEstimate? _agent;
    private bool _agentRead;

    private Task _initialLoad = Task.CompletedTask;
    private bool _disposed;

    public ProjectPage(ProjectsContext context, string projectId, bool beginRename)
    {
        _context = context;
        _projectId = projectId;
        InitializeComponent();

        ConfigureHeader();
        ConfigureActions();

        _context.Store.Changed += OnStoreChanged;
        _context.Workspace.Readings.ReadingChanged += OnReadingChanged;
        _context.Workspace.Board.Changed += OnBoardChanged;

        Apply();
        StartTimer();

        // "Rename" in a card's menu opens the page with the name already a text
        // box (spec, Renaming).
        if (beginRename) NameEditor.BeginEdit();

        _initialLoad = LoadAsync(refreshMenu: false, estimateAgent: true);
    }

    private ProjectEntry? Project => _context.Store.Find(_projectId);

    // F5 reads everything again, the estimate included: the person who edited
    // AGENTS.md wants to see the number move. The window coming back to the
    // front reads the folder again, and the menu only if it was not found, since
    // the person may have just run the install command in a terminal.
    public Task RefreshAsync(RefreshReason reason) => reason == RefreshReason.Requested
        ? LoadAsync(refreshMenu: true, estimateAgent: true)
        : LoadAsync(refreshMenu: _listing is not ScriptMenuListing.Listed, estimateAgent: false);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimer();
        _context.Store.Changed -= OnStoreChanged;
        _context.Workspace.Readings.ReadingChanged -= OnReadingChanged;
        _context.Workspace.Board.Changed -= OnBoardChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    // Reads the folder, the menu and the estimate, in parallel, and draws the
    // page once they are in. A reading that cannot be made leaves the previous
    // values on screen (the engine says why in the log).
    //
    // The menu and the estimate skip a folder the cached reading calls missing.
    // When it did, the folder is read first: a folder that came back would
    // otherwise get its readouts but not its menu nor its estimate until a
    // second refresh.
    private async Task LoadAsync(bool refreshMenu, bool estimateAgent)
    {
        if (_disposed || Project is not { } project) return;

        try
        {
            Task reading = _context.RefreshReadingsAsync([project], _lifetime.Token);
            if (FolderIsMissing(project)) await reading;

            await Task.WhenAll(
                reading,
                ReadMenuAsync(project, refreshMenu),
                estimateAgent ? EstimateAgentAsync(project) : Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
            // The person left the page.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // One reading that fails leaves the others and the page as they
            // were; the log says what kind of failure it was.
            DeckleProjectsSource.Log.ProjectsPageFailed("project", ex.GetType().Name);
        }

        if (!_disposed) Apply();
    }

    private async Task ReadMenuAsync(ProjectEntry project, bool refresh)
    {
        // A folder that is gone has no menu to read, and nothing to start.
        if (FolderIsMissing(project))
        {
            _listing = null;
            return;
        }

        ScriptMenuCache menus = _context.Workspace.Menus;
        _listing = await (refresh ? menus.RefreshAsync(project, _lifetime.Token) : menus.GetAsync(project, _lifetime.Token));
    }

    private async Task EstimateAgentAsync(ProjectEntry project)
    {
        if (FolderIsMissing(project)) return;

        _agent = await _context.Workspace.EstimateAgentTokensAsync(project.Id, _lifetime.Token);
        _agentRead = true;
    }

    // Draws the whole page from what the store, the readings and the board say
    // now. Cheap enough to run for any change: the controls it sets keep what
    // did not change (the cards stay while the pins are the same).
    private void Apply()
    {
        if (_disposed || Project is not { } project) return;

        bool missing = FolderIsMissing(project);
        ApplyHeader(project, missing);
        ApplyMissing(missing);
        if (missing) return;

        ApplyReadouts(project);
        ApplyOpenIn(project);
        ApplyPinned(project);
    }

    private void OnStoreChanged() => _context.Dispatch(Apply);

    private void OnReadingChanged(string projectId)
    {
        if (projectId == _projectId) _context.Dispatch(Apply);
    }

    private void OnBoardChanged(string projectId)
    {
        if (projectId == _projectId) _context.Dispatch(ApplyActions);
    }

    // The board moved or a second went by: only what shows an action's state is
    // drawn again.
    private void ApplyActions()
    {
        if (_disposed || Project is not { } project) return;
        bool missing = FolderIsMissing(project);
        ApplyHeaderAction(project, missing);
        if (!missing) ApplyPinned(project);
    }
}
