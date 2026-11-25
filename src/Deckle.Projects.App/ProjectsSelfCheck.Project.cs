using Deckle.Projects;
using Deckle.Projects.Views;

namespace Deckle.Projects.App;

// The Projects walk's project page: settled with a main action and pinned
// actions (P1), three versions and no script menu with the list of versions open
// (P2), an action running after another failed (P3) and the name being edited
// (P4).
//
// P3 runs nothing. The board is given the two runs the way the runner would
// have written them, then the page is opened and draws what it reads: that is
// the page's whole job, and no terminal is asked for.
internal static partial class ProjectsSelfCheck
{
    private static async Task WalkProjectsProjectAsync(
        ProjectsWindow window, ProjectsWorkspace workspace, ProjectsSample sample, ProjectsCapture capture)
    {
        // The readouts below need the git command line and the script menu a
        // shell; a machine without them draws the page with those lines empty,
        // and the checks then speak only of what is there.
        bool git = sample.GitAvailable;
        bool menu = workspace.Shell is not null;

        // Board P1. The project with a main action: the accent button under the
        // header, its readouts, its links and its pinned actions.
        await capture("p1", async () =>
        {
            await window.ShowProjectForReviewAsync(SampleProjectNames.Deckle);
            IReadOnlyList<string> page = window.ProjectOutlineForReview;
            ProjectsCheck(window.CurrentKindForReview == "project", "The window is not on the project's page.");
            ProjectsCheck(window.BackButtonShownForReview, "The back button is missing on a project's page.");
            ProjectsCheck(page.Contains("name: " + SampleProjectNames.Deckle), "The page does not show the project's name.");
            ProjectsCheck(page.Any(l => l.StartsWith("agent: ")), "The page does not show what an agent reads.");
            if (git) ProjectsCheck(page.Contains("version: v0.131.0"), "The page does not show the highest tag as the version.");
            if (git) ProjectsCheck(page.Contains("uncommitted: 3 files"), "The page does not count the three uncommitted files.");
            if (menu) ProjectsCheck(page.Any(l => l.StartsWith("accent: Build & run")), "The main action is not the accent button.");
        });

        // Board P2. Three versions that disagree and no script menu: the tag, a
        // Cargo manifest and a Gradle file. The list of versions is a flyout, so
        // the image keeps it.
        await capture("p2", async () =>
        {
            await window.ShowProjectForReviewAsync(SampleProjectNames.VoxelMods);
            IReadOnlyList<string> page = window.ProjectOutlineForReview;
            if (git)
            {
                ProjectsCheck(window.OpenVersionsFlyoutForReview(), "The page has no list of versions to open.");
                await Task.Delay(300);
                ProjectsCheck(window.VersionsFlyoutIsOpenForReview, "The list of versions did not open.");
                ProjectsCheck(ProjectsPopupsOpen(window) > 0, "The list of versions is not a popup over the page.");
            }
            ProjectsCheck(page.Any(l => l.StartsWith("no menu: ")), "The page does not say the project has no script menu.");
        }, keepsPopup: true);
        window.CloseVersionsFlyoutForReview();

        // Board P3. "Build & run" failed four minutes ago (it is the main
        // action; its failure is kept but the header's line says it waits), and
        // "Self-check" has run for 1 min 12 s: "Update prod" waits for it.
        ProjectEntry deckle = workspace.Store.FindByPath(sample.PathOf(SampleProjectNames.Deckle))
            ?? throw new InvalidOperationException("The sample's project is not in the store.");
        await capture("p3", async () =>
        {
            DateTimeOffset now = workspace.Clock.GetUtcNow();
            workspace.Board.Begin(deckle.Id, new ActionRun(
                1, new ActionRef("build-run.1"), "Build & run", now - TimeSpan.FromMinutes(5), "Build & run (self-check)"));
            workspace.Board.End(deckle.Id, RunOutcome.Failed, 1, now - TimeSpan.FromMinutes(4));
            workspace.Board.Begin(deckle.Id, new ActionRun(
                2, new ActionRef("self-check.0", "Debug"), "Self-check", now - TimeSpan.FromSeconds(72), "Self-check (self-check)"));

            await window.ShowProjectForReviewAsync(SampleProjectNames.Deckle);
            IReadOnlyList<string> page = window.ProjectOutlineForReview;
            ProjectsCheck(page.Any(l => l.StartsWith("main line: ") && l.Contains("waits for Self-check to end")),
                "The line under the header does not say the main action waits for Self-check.");
            if (menu)
            {
                ProjectsCheck(page.Any(l => l.StartsWith("accent: Build & run") && l.EndsWith("(off)")),
                    "The main action's button is not off while another action runs.");
                ProjectsCheck(page.Any(l => l.Contains("Running") && l.Contains("1 min 12 s")),
                    "The running action's card does not show its time.");
            }
        });
        // Nothing of the runs stays for the next state: P4 shows both actions
        // as not run yet.
        workspace.Board.Forget(deckle.Id);

        // Board P4. The same page, the name turned into a text box with its focus.
        await capture("p4", async () =>
        {
            await window.ShowProjectForReviewAsync(SampleProjectNames.Deckle, beginRename: true);
            if (!window.IsRenamingForReview) await window.BeginRenameForReviewAsync();
            await Task.Delay(200);
            ProjectsCheck(window.IsRenamingForReview, "The project's name is not being edited.");
        });
    }
}
