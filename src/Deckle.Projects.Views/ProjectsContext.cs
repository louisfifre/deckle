using Deckle.Shell;
using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// What a page receives from the window: the workspace it reads, the way to move
// to another place, the hooks to the neighbouring windows, and the few gestures
// that more than one page performs (adding a project, running the main
// action), written once here so Home, the category page and the project page
// cannot drift apart.
internal sealed class ProjectsContext(IProjectsHost host, ProjectsWorkspace workspace, ProjectsHooks hooks)
{
    public ProjectsWorkspace Workspace { get; } = workspace;

    public ProjectStore Store => Workspace.Store;

    public ProjectsHooks Hooks { get; } = hooks;

    public XamlRoot? XamlRoot => host.XamlRoot;

    // Whether the "Without a category" fold of Home is open. It is closed when
    // the window opens (spec) and stays as the person left it while the window
    // lives, since a page is built again at every visit of Home.
    public bool WithoutCategoryOpen { get; set; }

    public void Navigate(ProjectsTarget target) => host.NavigateTo(target);

    public void GoBack() => host.GoBack();

    // Runs the callback on the window's thread, always later: a handler that
    // changes the store must not have the page rebuilt from inside the gesture
    // that is still using it (a drag that ends on a card the rebuild removed).
    public void Dispatch(Action callback) =>
        host.Dispatcher.TryEnqueueOrLog(() => callback(), "PROJECTS", "page update");

    // "Add a project...": one folder, nothing asked about it (spec, Journeys).
    // A folder that is already a project opens its page and adds nothing; a new
    // one lands in the category the call names, or in none. Returns the project
    // that was added, or null when the person cancelled or it was known.
    public async Task<ProjectEntry?> AddProjectAsync(string? categoryId = null)
    {
        string? path = await host.PickFolderAsync();
        if (string.IsNullOrEmpty(path)) return null;

        AddResult added = Workspace.AddProject(path, categoryId);
        if (added.AlreadyKnown)
        {
            Navigate(new ProjectsTarget.Project(added.Project.Id));
            return null;
        }

        // The person must see the card they just added: the fold that holds it
        // opens (a choice of this build, the spec says only where it lands).
        if (added.Project.CategoryId is null) WithoutCategoryOpen = true;
        _ = Workspace.Readings.RefreshAsync([added.Project], CancellationToken.None);
        return added.Project;
    }

    // The button of a card or of a row: the same call as the page's accent
    // button. A refusal is the engine's safety net behind buttons that are
    // already disabled for the same reasons, so nothing is shown for it. The
    // caller does not await it (a click handler), so a failure is caught and
    // logged here instead of staying in a task nobody observes.
    public async Task RunMainActionAsync(string projectId)
    {
        try
        {
            await Workspace.StartMainActionAsync(projectId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DeckleProjectsSource.Log.ProjectsPageFailed("card", ex.GetType().Name);
        }
    }

    // Reads the projects again off the window's thread. A reading that cannot
    // be made leaves the previous one in place (the engine says so in the log).
    public Task RefreshReadingsAsync(IEnumerable<ProjectEntry> projects, CancellationToken cancellation) =>
        Workspace.Readings.RefreshAsync(projects, cancellation);
}
