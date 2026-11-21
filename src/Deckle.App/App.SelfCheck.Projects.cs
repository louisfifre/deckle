using System.Diagnostics;
using Deckle.App.SelfCheck;
using Deckle.Hosting.SelfCheck;
using Deckle.Core;
using Deckle.Modules;
using Deckle.Projects;
using Deckle.Projects.Views;
using Deckle.Vision;

namespace Deckle.App;

// The self-check's walk of the Deckle Projects window, one state for each board
// the maintainer drew, named after the board's code: h0 (no project yet), h1
// (Home), h2 (a wide window with the pane open), h3 (the fold of projects with
// no category opened and a card's "..." menu), c1 (a category's page), p1 to p4
// (a project's page: settled, three versions and no script menu, an action
// running after another failed, renaming).
//
// What the window works on is built here, never taken from the person's disk:
//
//   data      the run's own root, so projects.json is not the maintainer's
//   projects  synthetic folders made under that root (ProjectsSampleWorld),
//             some of them real Git repositories made with the git command line
//   terminal  one that opens nothing, so no click of the walk can start a
//             process outside this one (the walk presses no "Run" either)
//   clock     fixed, so "Running · 1 min 12 s" and "failed 4 min ago" read the
//             same on every run
//   links     none that opens anything
//
// P3 is not reached by running anything: the board is given the runs, the way
// the runner would, and the page draws what it reads.
//
// A state that fails to show fails its image, and so the run. The files beside
// this one hold the states by page: Home, the category page, the project page.
public partial class App
{
    // The moment the walk's clock stands at. Any date does; the lines are
    // computed from it ("4 min ago" is this minus the end of the run).
    private static readonly DateTimeOffset ProjectsReviewNow = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    // The size the boards are drawn at, in DIPs, and the wide one of H2.
    private const int ProjectsDrawnWidth = 1280;
    private const int ProjectsWideWidth = 1600;
    private const int ProjectsDrawnHeight = 900;

    // Reaches a state, then photographs it unless the gate says another state is
    // the target. A state whose subject is a popup keeps it for the image: every
    // other image closes the popups first (the walk leaves some open).
    private delegate Task ProjectsCapture(string state, Func<Task>? reach = null, bool keepsPopup = false);

    private static void ProjectsCheck(bool holds, string what)
    {
        if (!holds) throw new InvalidOperationException(what);
    }

    private async Task WalkProjectsAsync(
        SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step,
        SelfCheckWalkGate gate)
    {
        var clock = Stopwatch.StartNew();
        using var workspace = new ProjectsWorkspace(
            AppPaths.GetModuleDirectory(ModuleIds.Projects),
            new ReviewClock(ProjectsReviewNow),
            new NoTerminalLauncher(),
            new NoProjectOpener(),
            watchRuns: false);

        // A hook that does nothing, so "Deckle Folders" shows on the project's
        // page as it does when the Atelier is there; pressing it opens nothing.
        var hooks = new ProjectsHooks { OpenInFolders = _ => Task.CompletedTask };

        ProjectsWindow window = CompleteProjectsWindow(() => new ProjectsWindow(workspace, hooks));
        OffscreenWindow.ShowInactive(window);
        bool loaded = await WaitUntilLoadedAsync(window);
        if (loaded) ResizeProjects(window, ProjectsDrawnWidth, ProjectsDrawnHeight);

        async Task Capture(string state, Func<Task>? reach = null, bool keepsPopup = false)
        {
            gate.ThrowIfDone();
            bool photographed = gate.Photographs(state);
            step($"projects/{state}");
            var stateClock = Stopwatch.StartNew();
            if (!loaded)
            {
                report.Surfaces.Add(new SelfCheckSurface("projects", state, null, false, clock.ElapsedMilliseconds, null, 0, 0, "window-capture"));
                return;
            }
            try
            {
                if (reach is not null) await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("projects", state, null, false, stateClock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            if (!photographed)
            {
                if (keepsPopup) await CloseTransientPopupsAsync(window);
                return;
            }
            (int closed, int left) = keepsPopup ? (0, 0) : await CloseTransientPopupsAsync(window);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, window, "projects", state, null, true, stateClock.ElapsedMilliseconds, closed, left));
            report.Save(outputFolder);
            if (keepsPopup) await CloseTransientPopupsAsync(window);
        }

        // The window opens with no project: the data root is the run's own.
        // (A run kept with -KeepData may already hold some; the empty screen is
        // then not there to photograph.)
        ProjectsCapture capture = Capture;
        if (workspace.Store.Projects.Count == 0) await WalkProjectsHomeEmptyAsync(window, capture);

        ProjectsSample sample = ProjectsSampleWorld.Create(Path.Combine(AppPaths.UserDataRoot, "self-check-sample-projects"));
        if (workspace.Store.Projects.Count == 0) sample.Populate(workspace.Store);

        await WalkProjectsHomeAsync(window, capture);
        await WalkProjectsCategoryAsync(window, capture);
        await WalkProjectsProjectAsync(window, workspace, sample, capture);
    }

    private static void ResizeProjects(ProjectsWindow window, int widthDip, int heightDip)
    {
        double scale = window.Content.XamlRoot?.RasterizationScale ?? 1.0;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(widthDip * scale), (int)(heightDip * scale)));
    }

    // A clock that stands still. The runs, the readings and the lines under the
    // buttons all ask the workspace's clock, so with this one two runs of the
    // walk draw the same words.
    private sealed class ReviewClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // The terminal the walk gives the workspace: it opens nothing and says so.
    private sealed class NoTerminalLauncher : ITerminalLauncher
    {
        public TerminalStart Launch(TerminalCommand command) =>
            TerminalStart.Failed(TerminalStart.Console, "self-check");
    }

    // The links the walk gives the workspace: none opens anything.
    private sealed class NoProjectOpener : IProjectOpener
    {
        public bool OpenInExplorer(string projectPath) => false;

        public bool OpenInTerminal(string projectPath) => false;

        public bool OpenScriptMenu(string projectPath) => false;

        public bool OpenWebPage(Uri page) => false;
    }
}
