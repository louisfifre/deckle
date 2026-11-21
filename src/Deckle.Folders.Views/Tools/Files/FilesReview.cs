using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// What the self-check drives to photograph the file tool in its displays.
// The step takes the path a click on a display button takes. The view is
// found in the window's tree, so the window itself needs no hook for one tool.
public static class FilesReview
{
    // Returns once the next layout has realized the display's containers and
    // their icons have been asked for.
    public static async Task ShowDisplayAsync(Window window, FileListDisplay display)
    {
        Find(window).ShowDisplay(display);
        await Task.Delay(300);
    }

    // The row of that name with its two actions shown, as the pointer over it
    // shows them; nothing is pressed. Throws when the row is not on screen,
    // so the image that should show it fails.
    public static async Task ShowRowActionsAsync(Window window, string name)
    {
        if (!Find(window).ShowRowActionsForReview(name))
            throw new InvalidOperationException($"The row {name} is not on screen in the file tool.");
        await Task.Delay(100);
    }

    public static void HideRowActions(Window window) => Find(window).HideRowActionsForReview();

    // Several items selected as Ctrl+click on each selects them, with the
    // panel of the group. Throws when one is not in the folder.
    public static async Task SelectAsync(Window window, params string[] names)
    {
        if (!Find(window).SelectForReview(names))
            throw new InvalidOperationException($"One of {string.Join(", ", names)} is not in the file tool's folder.");
        await Task.Delay(200);
    }

    // Throws when the name of that item, on screen in the shown display, has
    // no tooltip with its full text, so the image of a long name fails
    // without one: a tooltip is a popup, which no image keeps open.
    public static async Task RequireNameToolTipAsync(Window window, string name)
    {
        await Task.Delay(200);
        if (Find(window).CheckNameToolTipForReview(name) is { } problem)
            throw new InvalidOperationException(problem);
    }

    // Waits until the tile of each item, on screen in the gallery, shows the
    // preview of its content: a picture for a PDF, its text for a Markdown
    // file. Throws after a few seconds with the items still without one, so
    // the image of the previews fails rather than shows icons.
    public static async Task RequirePreviewsAsync(Window window, params string[] names)
    {
        FilesView view = Find(window);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (view.MissingPreviewsForReview(names) is []) return;
            await Task.Delay(150);
        }
        throw new InvalidOperationException(
            $"No preview on the gallery tile of {string.Join(", ", view.MissingPreviewsForReview(names))}.");
    }

    private static FilesView Find(Window window) =>
        FindIn(window.Content) ?? throw new InvalidOperationException("The file tool is not shown in this window.");

    private static FilesView? FindIn(DependencyObject? element)
    {
        if (element is null) return null;
        if (element is FilesView view) return view;
        // A view just placed in the window's centre is its ContentPresenter's
        // content before the next layout makes it a visual child.
        if (element is Microsoft.UI.Xaml.Controls.ContentPresenter { Content: FilesView placed }) return placed;
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            if (FindIn(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        }
        return null;
    }
}
