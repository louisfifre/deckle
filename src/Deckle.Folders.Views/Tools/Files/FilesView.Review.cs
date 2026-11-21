using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

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

    // Selects these items as Ctrl+click on each would, in that order, so the
    // last one is the path the window keeps. False when one is not in the
    // folder.
    internal bool SelectForReview(IReadOnlyList<string> names)
    {
        FileRow?[] rows = [.. names.Select(n => _rows.FirstOrDefault(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase)))];
        if (rows.Any(r => r is null)) return false;
        ListViewBase items = ItemsOf(Display);
        items.SelectedItems.Clear();
        foreach (FileRow? row in rows) items.SelectedItems.Add(row);
        items.ScrollIntoView(rows[^1]);
        return true;
    }

    // Null when the item's name, on screen in the shown display, follows the
    // rule: in the list and the large rows, a tooltip with the full text when
    // the ellipsis cuts it and none when it is whole; in the gallery, the
    // tile's tooltip. Otherwise what is wrong, for the image that should show
    // it to fail with.
    internal string? CheckNameToolTipForReview(string name)
    {
        FileRow? row = _rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (row is null) return $"The item {name} is not in the folder.";
        ListViewBase items = ItemsOf(Display);
        items.ScrollIntoView(row);
        items.UpdateLayout();
        if (items.ContainerFromItem(row) is not DependencyObject container) return $"The item {name} is not on screen.";
        if (All<TextBlock>(container).FirstOrDefault(t => t.Text == row.Name && TrimmedNameToolTip.GetIsEnabled(t)) is { } shown)
        {
            object? tip = ToolTipService.GetToolTip(shown);
            if (shown.IsTextTrimmed) return tip as string == row.Name ? null : $"The name of {name} is cut in the {Display} display and has no tooltip.";
            return tip is null ? null : $"The name of {name} is whole in the {Display} display and still has a tooltip.";
        }
        foreach (FrameworkElement element in All<FrameworkElement>(container))
        {
            if (ToolTipService.GetToolTip(element) is string text && text == row.Name) return null;
        }
        return $"The name of {name} has no tooltip in the {Display} display.";
    }

    // The names among these whose gallery tile has no preview of its content
    // yet, a picture or a text; each is scrolled into view first, which
    // realizes its tile and so asks for its preview. Empty once they all have
    // one.
    internal IReadOnlyList<string> MissingPreviewsForReview(IReadOnlyList<string> names)
    {
        ListViewBase items = ItemsOf(Display);
        var missing = new List<string>();
        foreach (string name in names)
        {
            FileRow? row = _rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (row is null || Display != FileListDisplay.Gallery)
            {
                missing.Add(name);
                continue;
            }
            items.ScrollIntoView(row);
            if (row.Thumbnail is null && row.PreviewText is null) missing.Add(name);
        }
        return missing;
    }

    private static IEnumerable<T> All<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T found) yield return found;
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            foreach (T inner in All<T>(VisualTreeHelper.GetChild(element, i))) yield return inner;
        }
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
