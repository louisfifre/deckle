using Deckle.Projects;
using Deckle.Projects.Views;

namespace Deckle.Projects.App;

// The Projects walk's category page (board C1): the title, the projects of the
// category in the order the person keeps them, and the button that deletes the
// category.
internal static partial class ProjectsSelfCheck
{
    private static async Task WalkProjectsCategoryAsync(ProjectsWindow window, ProjectsCapture capture)
    {
        await capture("c1", async () =>
        {
            ProjectsCheck(window.ShowCategoryForReview(SampleProjectNames.Development),
                "The store holds no category by the name the walk asked for.");
            await window.RefreshForReviewAsync();
            ProjectsCheck(window.CurrentKindForReview == "category", "The window is not on the category's page.");
            ProjectsCheck(window.BackButtonShownForReview, "The back button is missing on a category's page.");
            ProjectsCheck(window.DeleteCategoryButtonForReview is not null, "The category's page has no delete button.");
        });
    }
}
