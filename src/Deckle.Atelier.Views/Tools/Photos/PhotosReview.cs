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
    // Sets the mark of the named photos as K or X would on a selection, and
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

    // ── Descriptions ──────────────────────────────────────────────────────
    //
    // The panel's buttons and the folder command, on the selected photo and
    // the folder shown. The self-check gives the app a model of its own that
    // answers what the walk wants and never reaches a server.

    // "Describe this photo" on the selected photo; returns once its panel
    // reads "Describing…" or the description has ended.
    public static async Task DescribeSelectedAsync(Window window)
    {
        (PhotoDescriptions descriptions, PhotoFile photo) = Selected(window);
        _ = descriptions.DescribeAsync(photo);
        await Task.Delay(100);
    }

    // "Stop" in the selected photo's panel.
    public static void StopSelected(Window window)
    {
        (PhotoDescriptions descriptions, PhotoFile photo) = Selected(window);
        descriptions.Stop(photo);
    }

    // Waits until the selected photo's description is no longer under way;
    // throws past the timeout, so the image that should show it fails.
    public static async Task WaitForSelectedAsync(Window window, TimeSpan timeout)
    {
        (PhotoDescriptions descriptions, PhotoFile photo) = Selected(window);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (descriptions.StateOf(photo).InProgress)
        {
            if (clock.Elapsed > timeout) throw new TimeoutException("The photo's description did not end.");
            await Task.Delay(50);
        }
        await Task.Delay(100);
    }

    // "Describe the folder"; returns once the run is under way.
    public static Task DescribeFolderAsync(Window window) => Find(window).DescribeFolderAsync();

    // Waits until the folder's run has described at least that many photos.
    public static async Task WaitForFolderAsync(Window window, Func<PhotoDescriptionRun?, bool> reached, TimeSpan timeout)
    {
        PhotosView view = Find(window);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!reached(view.CurrentRunForReview))
        {
            if (clock.Elapsed > timeout) throw new TimeoutException("The folder's description did not reach the state.");
            await Task.Delay(50);
        }
        await Task.Delay(150);
    }

    // "Stop describing the folder" beside the folder's progress bar.
    public static void StopFolder(Window window) => Find(window).StopFolderForReview();

    private static (PhotoDescriptions, PhotoFile) Selected(Window window)
    {
        PhotosView view = Find(window);
        PhotoDescriptions descriptions = view.DescriptionsForReview
            ?? throw new InvalidOperationException("The app has no model to describe photos.");
        PhotoFile photo = view.SelectedPhotoForReview
            ?? throw new InvalidOperationException("No single photo is selected.");
        return (descriptions, photo);
    }

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
