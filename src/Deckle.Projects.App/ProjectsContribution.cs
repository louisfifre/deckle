using Deckle.Contributions;
using Deckle.Core;
using Deckle.Modules;
using Deckle.Projects.Views;

namespace Deckle.Projects.App;

// The Deckle Projects window, the only window of its app: created on first use
// by the host's lazy path, placed where the person left it. Its data lives in
// the module's folder of this app's own data root
// (apps\projects\modules\projects).
//
// The workspace (the store, the readings, the run board) is the app's, built
// once with the window and kept for the process: closing the window ends the
// app, and the workspace with it. An action keeps running in its terminal; what
// ends is this app following it (App.OnLaunched says why).
//
// The terminal: the one choice terminal.json holds in this module folder
// (TerminalChoiceFile), Windows Terminal when absent. This app has no Settings
// window yet, so nothing edits it from the interface.
//
// No hook: "Open in Deckle Folders" stays absent, Deckle Folders being another
// app this one does not reach.
internal sealed class ProjectsContribution : IContribution
{
    // The module's data folder under the app's root, and the contribution's id.
    internal const string ModuleId = "projects";

    private ProjectsWorkspace? _workspace;

    public ProjectsContribution()
    {
        Windows =
        [
            new ContributedWindow
            {
                Name = "projects",
                Create = () => new ProjectsWindow(Workspace, new ProjectsHooks()),
                Show = window => ((ProjectsWindow)window).ShowAndActivate(),
            },
        ];
    }

    public string Id => ModuleId;

    // Not a module of a catalogue: this app has no module chooser.
    public ModuleDescriptor? Descriptor => null;

    public IReadOnlyList<ContributedWindow> Windows { get; }

    // The walk of the window, moved here from the published Deckle with the
    // window: its states (h0 to p4) are offered when a target names one it
    // does not have.
    public IReadOnlyList<ContributedWalk> SelfCheckWalks { get; } =
    [
        new ContributedWalk
        {
            WindowName = "projects",
            ShowsViewsAtRun = true,
            Run = context => ProjectsSelfCheck.RunProjectsWalkAsync(ProjectsWalkContext.From(context)),
        },
    ];

    // How the workspace is made on first use. The self-check replaces it before
    // the window is asked for, with one on its run's root, a fixed clock and a
    // terminal that opens nothing.
    internal Func<ProjectsWorkspace> CreateWorkspace { get; set; } =
        () => new ProjectsWorkspace(AppPaths.GetModuleDirectory(ModuleId));

    internal ProjectsWorkspace Workspace => _workspace ??= CreateWorkspace();

    // The actions running now, across the projects; zero before the window
    // was ever built.
    internal int RunsGoingOn
    {
        get
        {
            if (_workspace is not { } workspace) return 0;
            return workspace.Store.Projects.Count(p => workspace.Board.ActiveRun(p.Id) is not null);
        }
    }

    // Stops following the actions; they go on in their terminals.
    internal void ReleaseWorkspace()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    public void Compose(IContributionContext context)
    {
        // Nothing to plant: the window builds its workspace on first open.
    }
}
