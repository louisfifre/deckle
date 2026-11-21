using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Modules;
using Deckle.Projects;
using Deckle.Projects.Views;

namespace Deckle.App;

// The Deckle Projects window, opened from the tray like the Atelier's: created
// on first use by the same lazy path, placed where the person left it, and
// dropped on close so the next open builds a fresh one. The module composes
// nothing at start: with no window open it costs nothing. Its data lives in
// the module's own folder of the data root.
//
// Closing the window never stops an action: it runs in its own terminal. The
// workspace that watches it (the run board, the last results) belongs to the
// app, not to the window: it is built on the first open and kept until Deckle
// closes, so a window reopened mid-run still sees the run and the results
// (spec, Behaviours).
public partial class App
{
    private ProjectsWindow? _projectsWindow;
    private ProjectsWorkspace? _projectsWorkspace;

    private static bool ProjectsPresent => ModulePresence.IsPresent(ModuleIds.Projects);

    private void ShowProjectsWindowLazy()
    {
        if (!ProjectsPresent) return;
        EnsureProjectsWindow().ShowAndActivate();
        DeckleAppSource.Log.WindowOpened("projects");
    }

    private ProjectsWindow EnsureProjectsWindow()
        => _projectsWindow ?? CompleteProjectsWindow(() => new ProjectsWindow(
            EnsureProjectsWorkspace(), BuildProjectsHooks()));

    private ProjectsWorkspace EnsureProjectsWorkspace()
        => _projectsWorkspace ??= new ProjectsWorkspace(AppPaths.GetModuleDirectory(ModuleIds.Projects));

    // The window around a built ProjectsWindow: where it sits, what happens when
    // it closes, its theme. The self-check builds the window on a workspace of
    // its own (fake terminal, fixed clock) and shares this part, so the window
    // it photographs is wrapped exactly as the tray's.
    private ProjectsWindow CompleteProjectsWindow(Func<ProjectsWindow> create)
    {
        // Not gated: one Stopwatch per window per process, and the provider
        // declares this milestone ungoverned.
        var sw = Stopwatch.StartNew();
        var window = CreateWindowOrLog("projects", create);
        SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Projects);
        window.AppWindow.Closing += (_, _) =>
            SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Projects);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_projectsWindow, window)) _projectsWindow = null;
        };

        _projectsWindow = window;
        ApplyThemeToSingle(window);
        DeckleWindowingSource.Log.WindowLoadComplete("projects", sw.ElapsedMilliseconds);
        return window;
    }

    // Opens a folder in Deckle Folders: planted at start by the contribution
    // that carries that window (AtelierContribution.Compose), null while none
    // does.
    internal static Func<string, Task>? OpenInFoldersHook { get; set; }

    // What Deckle Projects reaches in its neighbours. A hook left null makes its
    // button absent from the page, so "Open in Deckle Folders" exists only while
    // a contribution plants it.
    private static ProjectsHooks BuildProjectsHooks() => new()
    {
        OpenInFolders = OpenInFoldersHook,
    };
}
