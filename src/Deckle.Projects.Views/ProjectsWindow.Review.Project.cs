namespace Deckle.Projects.Views;

// What the self-check drives to photograph a project's page (boards P1 to P4).
// Each step takes the path a click takes and awaits what it starts, so the image
// shows the state settled. The window's, the pane's and Home's steps are in
// ProjectsWindow.Review.cs.
//
// The run states of P3 are not seeded here: the self-check builds the workspace
// the window works on, so it begins and ends runs on that workspace's board
// itself, with a fake terminal and a fixed clock.
public sealed partial class ProjectsWindow
{
    private ProjectPage? ProjectPageShown => _page as ProjectPage;

    // Opens a project's page by the name it shows, as a click on its card does,
    // and returns once the page has read the folder, the menu and the estimate.
    // Fails when the project is unknown or the page could not be built.
    public async Task ShowProjectForReviewAsync(string name, bool beginRename = false)
    {
        string id = _workspace.Store.Projects.FirstOrDefault(p => p.DisplayName == name)?.Id
            ?? throw new InvalidOperationException("No project has the name the self-check asked for.");

        ShowForReview(new ProjectsTarget.Project(id, beginRename));
        if (ProjectPageShown is { } page) await page.SettledForReview;
    }

    // Turns the name into a text box, as clicking it does (board P4).
    public async Task BeginRenameForReviewAsync()
    {
        ProjectPageShown?.BeginRenameForReview();
        // The box takes its focus once it is laid out.
        await Task.Delay(100);
    }

    public bool IsRenamingForReview => ProjectPageShown?.IsRenamingForReview ?? false;

    // Opens the list of versions (board P2); false when the page has none.
    public bool OpenVersionsFlyoutForReview() => ProjectPageShown?.OpenVersionsForReview() ?? false;

    public void CloseVersionsFlyoutForReview() => ProjectPageShown?.CloseVersionsForReview();

    public bool VersionsFlyoutIsOpenForReview => ProjectPageShown?.VersionsFlyoutIsOpenForReview ?? false;

    // The project page, line by line, to compare with the board.
    public IReadOnlyList<string> ProjectOutlineForReview => ProjectPageShown?.OutlineForReview ?? [];
}
