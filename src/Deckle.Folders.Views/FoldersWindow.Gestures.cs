using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// The gestures of the folder groups as the self-check presses them: through
// each control's own UI Automation peer, the path a screen reader takes and
// the one a click ends in. A group's item is invoked as a click on its row
// invokes it, or expanded and collapsed as its chevron does; a "..." button is
// invoked as a click presses it; the "Group" entry of a folder's menu is
// expanded as the pointer resting on it opens it; a menu entry is invoked as
// a click chooses it. Nothing here sets the state those gestures change: the
// window's own handlers do, and the walk then reads what the window shows.
//
// The one difference from a person's click: a menu opened while the review
// keeps menus in the window is drawn inside it (ShowReviewMenu), so the
// off-screen window's capture sees it and no menu window reaches a monitor.
public sealed partial class FoldersWindow
{
    // The AutomationIds the gestures find their entries by. A group's choice
    // carries the group's id, which a name typed by the person cannot clash with.
    public const string GroupSubMenuId = "FolderMenu_Group";
    public const string NoGroupChoiceId = "FolderGroupChoice_None";
    public const string NewGroupChoiceId = "FolderGroupChoice_New";
    public const string RenameGroupEntryId = "FolderGroupMenu_Rename";
    public const string RemoveGroupEntryId = "FolderGroupMenu_Remove";

    public static string GroupChoiceId(string? groupId) =>
        groupId is null ? NoGroupChoiceId : "FolderGroupChoice_" + groupId;

    // Set by the first gesture of the review that opens a menu; never set in
    // a person's window.
    private bool _reviewMenusInWindow;

    // ── A group's row ────────────────────────────────────────────────────

    // A click on the group's row: NavigationView's own invoke, which folds an
    // expanded group and unfolds a folded one. False when the item or its
    // invoke pattern is missing.
    public bool ClickGroupForReview(string groupId) =>
        FindGroupItem(groupId) is { } parts && InvokeByAutomation(parts.Item);

    // The chevron, or the keyboard's arrow, through the item's expand and
    // collapse pattern.
    public bool ExpandGroupForReview(string groupId, bool expand)
    {
        if (FindGroupItem(groupId) is not { } parts) return false;
        if (PeerOf(parts.Item)?.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider chevron) return false;
        if (expand) chevron.Expand();
        else chevron.Collapse();
        return true;
    }

    // How many of the group's folders the pane holds, and how many of them
    // are drawn in it: laid out, with nothing between them and the pane
    // collapsed. A folded group draws none.
    public (int Shown, int Total) GroupFoldersShownForReview(string groupId)
    {
        if (FindGroupItem(groupId) is not { } parts) return (0, 0);
        List<FolderItem> inside = [.. _folderItems.Where(p => ReferenceEquals(p.Host, parts))];
        return (inside.Count(p => IsDrawnInPane(p.Item)), inside.Count);
    }

    private bool IsDrawnInPane(FrameworkElement element)
    {
        if (element.XamlRoot is null || element.ActualHeight <= 0) return false;
        for (DependencyObject? at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(at, Nav)) return true;
        }
        return false;
    }

    // ── The menus, opened by their "..." ─────────────────────────────────

    // The group's "..." pressed as a click presses it, with the pointer over
    // the row as it would be. Answers the entries of the menu that opened.
    public Task<IReadOnlyList<ReviewMenuEntry>> OpenGroupMenuByClickForReviewAsync(string groupId) =>
        FindGroupItem(groupId) is { } parts ? OpenMenuByClickAsync(parts) : Task.FromResult<IReadOnlyList<ReviewMenuEntry>>([]);

    public Task<IReadOnlyList<ReviewMenuEntry>> OpenFolderMenuByClickForReviewAsync(string path) =>
        FindFolderItem(path) is { } parts ? OpenMenuByClickAsync(parts) : Task.FromResult<IReadOnlyList<ReviewMenuEntry>>([]);

    private async Task<IReadOnlyList<ReviewMenuEntry>> OpenMenuByClickAsync(PaneItem parts)
    {
        _reviewMenusInWindow = true;
        CloseMenusForReview();
        parts.SetPointerOver(true);
        if (!InvokeByAutomation(parts.More)) return [];
        MenuFlyoutPresenter? presenter = null;
        await WaitForAsync(() => (presenter = OpenMenuPresenters().FirstOrDefault()) is { IsLoaded: true });
        return presenter is null ? [] : Entries(presenter);
    }

    // A submenu of the open menu, by its AutomationId, opened through its
    // expand pattern as the pointer resting on it opens it. Answers the
    // entries the submenu shows, none when it did not open.
    public async Task<IReadOnlyList<ReviewMenuEntry>> OpenSubMenuForReviewAsync(string automationId)
    {
        if (FindOpenEntry(automationId) is not MenuFlyoutSubItem sub) return [];
        if (PeerOf(sub)?.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expand) return [];
        expand.Expand();
        object? first = sub.Items.FirstOrDefault();
        MenuFlyoutPresenter? presenter = null;
        // The submenu's presenter shows the subitem's own entries; failing
        // that identity, the newest of two open menus.
        await WaitForAsync(() =>
        {
            List<MenuFlyoutPresenter> open = OpenMenuPresenters();
            presenter = open.FirstOrDefault(p => first is not null && p.Items.Contains(first))
                ?? (open.Count > 1 ? open[^1] : null);
            return presenter is { IsLoaded: true };
        });
        return presenter is null ? [] : Entries(presenter);
    }

    // An entry of an open menu or submenu, chosen as a click chooses it.
    // False when no open menu shows it, or its peer has no way to invoke it.
    public bool ChooseMenuEntryForReview(string automationId) =>
        FindOpenEntry(automationId) is { } entry && InvokeByAutomation(entry);

    // True while every open popup of the window is drawn inside it.
    public bool OpenPopupsInsideWindowForReview() =>
        Content?.XamlRoot is not { } root
        || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).All(p => p.IsConstrainedToRootBounds);

    public void CloseMenusForReview()
    {
        _reviewMenu?.Hide();
        _reviewMenu = null;
        HideMoreButtonsForReview();
    }

    // The menus the window shows now, the outer one first.
    private List<MenuFlyoutPresenter> OpenMenuPresenters()
    {
        if (Content?.XamlRoot is not { } root) return [];
        List<MenuFlyoutPresenter> found = [];
        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (FindDescendant<MenuFlyoutPresenter>(popup.Child) is { } presenter) found.Add(presenter);
        return found;
    }

    private MenuFlyoutItemBase? FindOpenEntry(string automationId) =>
        OpenMenuPresenters()
            .SelectMany(p => p.Items.OfType<MenuFlyoutItemBase>())
            .FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == automationId);

    // What a menu shows of each entry, separators left out. Drawn: laid out
    // with a height, as a person would see it.
    private static IReadOnlyList<ReviewMenuEntry> Entries(MenuFlyoutPresenter presenter) =>
    [
        .. presenter.Items.OfType<MenuFlyoutItemBase>().Where(e => e is not MenuFlyoutSeparator).Select(e => new ReviewMenuEntry(
            AutomationProperties.GetAutomationId(e),
            e switch { MenuFlyoutItem item => item.Text, MenuFlyoutSubItem sub => sub.Text, _ => "" },
            e.IsEnabled,
            e.ActualHeight > 0 && e.Visibility == Visibility.Visible,
            e switch { RadioMenuFlyoutItem radio => (bool?)radio.IsChecked, ToggleMenuFlyoutItem toggle => toggle.IsChecked, _ => null },
            e is MenuFlyoutSubItem)),
    ];

    // ── UI Automation ────────────────────────────────────────────────────

    private static AutomationPeer? PeerOf(UIElement element) =>
        FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);

    // The control's own invoke: a click for a button, a menu entry or a
    // navigation item. A radio entry of a menu may offer it as a toggle or a
    // selection instead, which ends in the same click.
    private static bool InvokeByAutomation(UIElement element)
    {
        AutomationPeer? peer = PeerOf(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
        else return false;
        return true;
    }

    private static T? FindDescendant<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        if (parent is T self) return self;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindDescendant<T>(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }

    // A popup opens and lays out over the next frames.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int waited = 0; !condition() && waited < 2000; waited += 50) await Task.Delay(50);
        await Task.Delay(100);
    }

    private GroupItem? FindGroupItem(string groupId) => _groupItems.FirstOrDefault(g => g.Group.Id == groupId);
}

// One entry of a menu the window shows, as the self-check reads it.
public sealed record ReviewMenuEntry(
    string AutomationId, string Text, bool IsEnabled, bool Drawn, bool? IsChecked, bool IsSubMenu);
