using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// What the self-check drives in the file tool beyond a click on a display
// button: a row with its actions shown, as the pointer over it shows them.
// Nothing here presses an action: the self-check never opens an application
// or File Explorer, and never sends an item to the Recycle Bin.
internal sealed partial class FilesView
{
    private FileRowActions? _reviewedActions;

    // False when the row is not in the folder or its container is not
    // realized.
    internal bool ShowRowActionsForReview(string name)
    {
        HideRowActionsForReview();
        FileRow? row = _rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (row is null) return false;
        var items = ItemsOf(Display);
        items.ScrollIntoView(row);
        items.UpdateLayout();
        if (items.ContainerFromItem(row) is not DependencyObject container) return false;
        _reviewedActions = Find<FileRowActions>(container);
        _reviewedActions?.SetPointerOver(true);
        return _reviewedActions is not null;
    }

    internal void HideRowActionsForReview()
    {
        _reviewedActions?.SetPointerOver(false);
        _reviewedActions = null;
    }

    private static T? Find<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T found) return found;
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(element, i)) is { } inner) return inner;
        }
        return null;
    }
}
