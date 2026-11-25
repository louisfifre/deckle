using Deckle.Projects;
using Deckle.Projects.Views;

namespace Deckle.Projects.App;

// The Projects walk's Home states: the empty screen (H0), Home with the pane
// closed (H1), a wide window with the pane open (H2) and the fold of projects
// with no category opened beside a card's "..." menu (H3).
internal static partial class ProjectsSelfCheck
{
    // Board H0: no project yet. No hamburger, no "New category": there is
    // nothing to list or to group.
    private static async Task WalkProjectsHomeEmptyAsync(ProjectsWindow window, ProjectsCapture capture)
    {
        await capture("h0", () =>
        {
            ProjectsCheck(window.HomeIsEmptyForReview, "Home does not show its empty screen on an empty store.");
            ProjectsCheck(!window.PaneToggleShownForReview, "The pane's button shows while there is no project to list.");
            ProjectsCheck(!window.BackButtonShownForReview, "The back button shows on Home.");
            return Task.CompletedTask;
        });
    }

    private static async Task WalkProjectsHomeAsync(ProjectsWindow window, ProjectsCapture capture)
    {
        // Board H1. The store was just filled: Home draws it on the next turn
        // of the window's thread, then every card is read once.
        await capture("h1", async () =>
        {
            await Task.Delay(500);
            window.ShowHomeForReview();
            await window.RefreshForReviewAsync();
            ProjectsCheck(!window.HomeIsEmptyForReview, "Home still shows its empty screen after projects were added.");
            ProjectsCheck(window.PaneToggleShownForReview, "The pane's button is missing on Home.");
            ProjectsCheck(!window.BackButtonShownForReview, "The back button shows on Home.");
            ProjectsCheck(!window.IsPaneOpenForReview, "The pane is open when the window opens.");
            ProjectsCheck(window.CurrentKindForReview == "home", "The window is not on Home.");
        });

        // Board H2. Wider than the others, with the pane open beside the page
        // (docked, not over it), listing the categories and their projects.
        await capture("h2", async () =>
        {
            ResizeProjects(window, ProjectsWideWidth, ProjectsDrawnHeight);
            window.SetPaneOpenForReview(true);
            await Task.Delay(600);
            await window.RefreshForReviewAsync();
            ProjectsCheck(window.IsPaneOpenForReview, "The pane did not open.");
            IReadOnlyList<string> outline = window.PaneOutlineForReview;
            ProjectsCheck(outline.Contains("All projects"), "The pane does not list \"All projects\".");
            ProjectsCheck(outline.Contains(SampleProjectNames.Development), "The pane does not list the first category.");
            ProjectsCheck(outline.Contains(" " + SampleProjectNames.Deckle), "The pane does not list a project under its category.");
            ProjectsCheck(outline.Contains("Without a category"), "The pane does not list the projects with no category.");
        });
        window.SetPaneOpenForReview(false);
        ResizeProjects(window, ProjectsDrawnWidth, ProjectsDrawnHeight);
        await Task.Delay(400);

        // Board H3. The fold is opened and the "..." menu of one of its cards is
        // open: a popup, so the image keeps it. The card exists only once the
        // fold's grid is laid out.
        await capture("h3", async () =>
        {
            window.ShowHomeForReview();
            window.SetWithoutCategoryOpenForReview(true);
            await window.RefreshForReviewAsync();

            bool shown = false;
            for (int attempt = 0; attempt < 30 && !shown; attempt++)
            {
                shown = window.ShowCardMenuForReview(SampleProjectNames.PersonalSite);
                if (!shown) await Task.Delay(100);
            }
            ProjectsCheck(shown, "The card of a project with no category is not drawn in the opened fold.");
            await Task.Delay(300);
            ProjectsCheck(ProjectsPopupsOpen(window) > 0, "The card's menu did not open.");
        }, keepsPopup: true);
        window.SetWithoutCategoryOpenForReview(false);
    }

    // The popups open over the window's page, the way the walk's own closing
    // counts them.
    private static int ProjectsPopupsOpen(ProjectsWindow window) =>
        window.Content?.XamlRoot is { } root
            ? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count
            : 0;
}
