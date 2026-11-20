using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// What the self-check drives to photograph the window in each of its states.
// Each step takes the path a click takes and awaits what it starts, so the image
// shows the state settled. The category page's and the project page's own steps
// are in their partial files (ProjectsWindow.Review.Category.cs and
// ProjectsWindow.Review.Project.cs); this file holds the window's, the pane's
// and Home's.
public sealed partial class ProjectsWindow
{
    // The kind of page shown: "home", "category" or "project".
    public string CurrentKindForReview => KindOf(_history.Current ?? new ProjectsTarget.Home());

    public bool CanGoBackForReview => _history.CanGoBack;

    public bool BackButtonShownForReview => AppTitleBar.IsBackButtonVisible;

    public bool PaneToggleShownForReview => AppTitleBar.IsPaneToggleButtonVisible;

    public bool IsPaneOpenForReview => _pane.IsOpen;

    // The pane as the hamburger opens or closes it.
    public void SetPaneOpenForReview(bool open) => _pane.SetOpen(open);

    // The labels of the pane's items in order, the projects of a node after it
    // and indented by one space per level, for a check that the pane lists what
    // the store holds.
    public IReadOnlyList<string> PaneOutlineForReview
    {
        get
        {
            List<string> lines = [];
            void Walk(IList<object> items, int level)
            {
                foreach (object entry in items)
                {
                    if (entry is not NavigationViewItem item) continue;
                    lines.Add(new string(' ', level) + item.Content);
                    Walk(item.MenuItems, level + 1);
                }
            }
            Walk(Nav.MenuItems, 0);
            return lines;
        }
    }

    // Home, as the pane's "All projects" shows it.
    public void ShowHomeForReview() => NavigateWithoutMotion(new ProjectsTarget.Home());

    private void NavigateWithoutMotion(ProjectsTarget target)
    {
        _showWithoutMotion = true;
        try { NavigateTo(target); }
        finally { _showWithoutMotion = false; }
    }

    // Refresh as F5 runs it, awaited: the readings are read again and the page
    // shows them.
    public Task RefreshForReviewAsync() => RefreshPageAsync(RefreshReason.Requested);

    // True while Home shows its empty screen (board H0).
    public bool HomeIsEmptyForReview => (_page as HomePage)?.IsEmptyStateShown ?? false;

    // Opens or closes the fold of projects with no category (board H3).
    public void SetWithoutCategoryOpenForReview(bool open) => (_page as HomePage)?.SetFoldOpenForReview(open);

    // Opens a card's "..." menu by the project's name, as a press on the button
    // does; false when no such card is drawn.
    public bool ShowCardMenuForReview(string projectName)
    {
        if ((_page as HomePage)?.CardForReview(projectName) is not { } card) return false;
        card.ShowMenuForReview();
        return true;
    }

    // What Home laid out, to compare with CardGridCapacity at the same width.
    public int HomeColumnsForReview => (_page as HomePage)?.FirstGridColumnsForReview ?? 0;

    public bool HomeShowsMoreTileForReview => (_page as HomePage)?.FirstGridShowsMoreTileForReview ?? false;

    // The window's size in DIPs, for a check that it opened at the size the
    // boards are drawn at.
    public Windows.Foundation.Size ContentSizeForReview =>
        Content is FrameworkElement root ? new Windows.Foundation.Size(root.ActualWidth, root.ActualHeight) : default;

    private void ThrowIfPageFailed(string kind)
    {
        if (LastPageFailure is { } failure)
            throw new InvalidOperationException($"The {kind} page failed to show.", failure);
    }

    // Shows a place as a click does and fails when its page could not be built.
    public void ShowForReview(ProjectsTarget target)
    {
        NavigateWithoutMotion(target);
        ThrowIfPageFailed(KindOf(target));
    }
}
