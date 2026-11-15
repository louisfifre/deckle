using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

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
