using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// What the self-check drives to photograph the photo tool in its states: the
// marks set, a photo seen large, the summary of the application open. Each
// step takes the path a gesture takes. The view is found in the window's
// tree, so the window itself needs no hook for one tool.
//
// The summary is opened and closed, never answered: the self-check never
// sends a file to the Recycle Bin.
public static class PhotosReview
{
    // Sets the mark of the named photos as G or X would on a selection, and
    // waits for the store to hold it.
    public static Task MarkAsync(Window window, IEnumerable<string> names, PhotoMark mark)
    {
        PhotosView view = Find(window);
        HashSet<string> wanted = new(names, StringComparer.OrdinalIgnoreCase);
        return view.SetMarkForReview([.. view.Tiles.Where(t => wanted.Contains(t.Name))], mark);
    }

    public static async Task ShowLargeAsync(Window window, string name)
    {
        PhotosView view = Find(window);
        view.OpenLargeForReview(name);
        await Task.Delay(100);
    }

    public static void LeaveLarge(Window window) => Find(window).LeaveLargeForReview();

    // Returns once the summary is on screen; false when there was nothing to
    // apply, so no summary opened.
    public static async Task<bool> OpenApplySummaryAsync(Window window)
    {
        PhotosView view = Find(window);
        var opened = new TaskCompletionSource<bool>();
        void OnOpened() => opened.TrySetResult(true);
        view.SummaryOpened += OnOpened;
        try
        {
            Task applying = view.ApplyAsync();
            Task first = await Task.WhenAny(opened.Task, applying, Task.Delay(TimeSpan.FromSeconds(5)));
            return first == opened.Task;
        }
        finally
        {
            view.SummaryOpened -= OnOpened;
        }
    }

    public static void CloseApplySummary(Window window) => Find(window).CloseSummary();

    private static PhotosView Find(Window window) =>
        FindIn(window.Content) ?? throw new InvalidOperationException("The photo tool is not shown in this window.");

    private static PhotosView? FindIn(DependencyObject? element)
    {
        if (element is null) return null;
        if (element is PhotosView view) return view;
        // A view just placed in the window's centre is its ContentPresenter's
        // content before the next layout makes it a visual child.
        if (element is Microsoft.UI.Xaml.Controls.ContentPresenter { Content: PhotosView placed }) return placed;
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            if (FindIn(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        }
        return null;
    }
}
