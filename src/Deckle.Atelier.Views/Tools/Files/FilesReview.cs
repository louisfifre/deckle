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
