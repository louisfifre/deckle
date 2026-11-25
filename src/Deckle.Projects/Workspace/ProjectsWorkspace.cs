namespace Deckle.Projects;

// Why a project was not removed.
public enum RemoveOutcome
{
    Removed,

    // An action of the project runs: removing it would leave its terminal with
    // no page and its result nowhere (spec, Behaviours).
    RefusedWhileRunning,

    NotFound,
}

// The one object the window builds and every page reads: the store, the
// readings, the script menus, the actions that run and the links, plus the
// rules that cross them, which none of the parts can enforce alone ("a project
// cannot be removed while an action of it runs", "an action that ended brings
// the readouts up to date").
//
// The parts are built from a module folder and the system. The seams the
// self-check and the tests inject (a clock, a terminal, the links, the reader of
// folders) go through the constructor; none of them is a setting.
//
// The app holds one workspace for its lifetime and gives it to each window it
// opens, so the run board survives a window closing. Disposing it (when Deckle
// closes) stops watching the actions and nothing else: an action lives in its
// terminal (spec, Behaviours).
public sealed class ProjectsWorkspace : IDisposable
{
    public ProjectsWorkspace(
        string moduleDirectory,
        TimeProvider? clock = null,
        ITerminalLauncher? launcher = null,
        IProjectOpener? opener = null,
        IProjectReader? reader = null,
        IScriptMenuReader? menuReader = null,
        string? shell = null,
        bool locateShell = true,
        bool watchRuns = true)
    {
        ModuleDirectory = moduleDirectory;
        Clock = clock ?? TimeProvider.System;
        Shell = shell ?? (locateShell ? ShellLocator.Find() : null);

        Store = new ProjectStore(moduleDirectory, Clock);
        Readings = new ProjectReadings(reader ?? new ProjectReader(clock: Clock));
        Menus = new ScriptMenuCache(menuReader ?? new ScriptMenuReader(Shell), Store);
        Board = new ActionRunBoard();

        Launcher = launcher ?? TerminalLaunchers.Create(TerminalChoiceFile.Read(moduleDirectory));
        Opener = opener ?? new ProjectOpener(Launcher, Shell);
        Runner = new ActionRunner(Board, Launcher, Path.Combine(moduleDirectory, "runs"), Shell, Clock, watch: watchRuns);

        Board.Changed += OnBoardChanged;
    }

    public string ModuleDirectory { get; }

    public TimeProvider Clock { get; }

    // The shell the menu and the actions run under, or null when the machine has
    // neither pwsh nor powershell.
    public string? Shell { get; }

    public ProjectStore Store { get; }

    public ProjectReadings Readings { get; }

    public ScriptMenuCache Menus { get; }

    public ActionRunBoard Board { get; }

    public ActionRunner Runner { get; }

    public ITerminalLauncher Launcher { get; }

    public IProjectOpener Opener { get; }

    // Adds a folder as a project. A folder that is already one comes back as
    // that project with AlreadyKnown, and nothing is added: the caller shows its
    // page.
    public AddResult AddProject(string path, string? categoryId = null) => Store.AddProject(path, categoryId);

    // Removes a project, unless one of its actions runs. Only the entry goes;
    // the readings, the results and the cached menu of the project go with it.
    public RemoveOutcome RemoveProject(string projectId)
    {
        if (Store.Find(projectId) is null) return RemoveOutcome.NotFound;
        if (Board.ActiveRun(projectId) is not null) return RemoveOutcome.RefusedWhileRunning;

        Store.RemoveProject(projectId);
        Readings.Forget(projectId);
        Board.Forget(projectId);
        Menus.Forget(projectId);
        return RemoveOutcome.Removed;
    }

    // Runs the project's main action: the button on a card and the accent button
    // of the page. Refused with the runner's reason when the project has no main
    // action or does not exist.
    public Task<StartOutcome> StartMainActionAsync(string projectId)
    {
        ProjectEntry? project = Store.Find(projectId);
        PinnedAction? main = project?.MainAction;
        return project is null || main is null
            ? Task.FromResult(new StartOutcome(null, StartRefusal.NotPinned))
            : StartIfListed(project, main);
    }

    // Runs a pinned action of the project.
    public Task<StartOutcome> StartPinnedActionAsync(string projectId, ActionRef action)
    {
        ProjectEntry? project = Store.Find(projectId);
        PinnedAction? pin = project?.Pinned.FirstOrDefault(p => p.Action == action);
        return project is null || pin is null
            ? Task.FromResult(new StartOutcome(null, StartRefusal.NotPinned))
            : StartIfListed(project, pin);
    }

    // A pin the last listing of the menu does not give (it vanished, or its id
    // now names another script) is not started: a run goes by the id, so it
    // would run something else. Before any listing nothing is known against the
    // pin, and the runner decides.
    private Task<StartOutcome> StartIfListed(ProjectEntry project, PinnedAction pin)
    {
        IReadOnlyList<ScriptMenuEntry>? listed = Menus.Cached(project.Id);
        return listed is not null && !listed.Any(e => e.GivesPin(pin))
            ? Task.FromResult(new StartOutcome(null, StartRefusal.NotPinned))
            : Runner.StartAsync(project, pin);
    }

    // The tokens "An agent always reads" shows, read once per opening of the
    // project's page, off the calling thread, and cancelled when the page is
    // left. Null when the folder could not be read.
    public Task<AgentEstimate?> EstimateAgentTokensAsync(string projectId, CancellationToken cancellation)
    {
        ProjectEntry? project = Store.Find(projectId);
        return project is null
            ? Task.FromResult<AgentEstimate?>(null)
            : AgentReading.EstimateAsync(project.Path, cancellation);
    }

    // An action that ended (or was refused after it began) brings the project's
    // readouts up to date: it may have built a version or changed files (spec,
    // Behaviours: "when an action ends").
    private void OnBoardChanged(string projectId)
    {
        if (Board.ActiveRun(projectId) is not null) return;
        ProjectEntry? project = Store.Find(projectId);
        if (project is null) return;

        _ = RefreshAfterRunAsync(project);
    }

    private async Task RefreshAfterRunAsync(ProjectEntry project)
    {
        try
        {
            await Readings.RefreshAsync([project], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DeckleProjectsSource.Log.ReadingFailed(ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        Board.Changed -= OnBoardChanged;
        Runner.Dispose();
        Readings.Dispose();
        Menus.Dispose();
    }
}
