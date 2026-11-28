using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Folders.Views;

// The folder groups in the window: the pane item that holds a group's folders,
// the "Add…" menu that makes one, the menus that move a folder into one or
// rename and remove one, and the dialog that names one. The store keeps the
// groups (AddedFolderStore); the pane and Home read their order from
// FolderGrouping, and this file only asks the store to change and syncs.
public sealed partial class FoldersWindow
{
    // Segoe Fluent Icons code points of the menus.
    private const string AddFolderGlyph = "\uE8F4";  // NewFolder, the add item's glyph before
    private const string GroupGlyph = "\uE8F1";      // Library, Windows' word for a group of folders
    private const string RenameGroupGlyph = "\uE8AC"; // Rename

    // A group the person folded, by id, for the window's life: a pane sync, or
    // the pane going to the rail and coming back, does not open it again.
    // Nothing of it is saved.
    private readonly HashSet<string> _collapsedGroups = [];

    // Set while the pane opens or closes: NavigationView folds its groups as it
    // reduces to the rail, which is not the person folding one.
    private bool _paneTransition;

    // One group's item: its name, the small chevron the rail draws it as, and
    // the "..." slot at its end. Its folders are the item's MenuItems, so the
    // opened pane expands and folds it with NavigationView's own chevron at the
    // right end, and the rail opens them in NavigationView's own flyout.
    // Invoking it never selects it.
    private GroupItem NewGroupItem()
    {
        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Button more = MoreButton();
        Grid content = GroupItemContent(out FrameworkElement chevron, name);
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MoreSlotWidth) });
        Grid.SetColumn(more, 2);
        content.Children.Add(more);

        var item = new NavigationViewItem
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            SelectsOnInvoked = false,
        };
        var group = new GroupItem(item, name, content, chevron, more);
        more.Click += (_, _) => ShowGroupMenu(group, more, null);
        item.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            if (args.TryGetPosition(item, out Windows.Foundation.Point at)) ShowGroupMenu(group, item, at);
            else ShowGroupMenu(group, item, null);
        };
        return group;
    }

    // The person folds or unfolds a group in the opened pane.
    private void OnGroupExpansionChanged(NavigationViewItemBase? container, bool expanded)
    {
        if (_paneTransition || !Nav.IsPaneOpen) return;
        if (container is not NavigationViewItem { Tag: FolderGroup group }) return;
        if (expanded) _collapsedGroups.Remove(group.Id);
        else _collapsedGroups.Add(group.Id);
    }

    // ── The menus ────────────────────────────────────────────────────────

    // The footer item's menu, above the item and flush with its left edge:
    //
    //   Add a folder…
    //   Add a folder group…
    private MenuFlyout BuildAddMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(FoldersText.Get("Folders_Add"), AddFolderGlyph, () => _ = PickAndAddFolderAsync()));
        menu.Items.Add(MenuItem(FoldersText.Get("Folders_AddGroup"), GroupGlyph, () => _ = NewGroupAsync(null)));
        return menu;
    }

    private void ShowAddMenu() =>
        BuildAddMenu().ShowAt(AddItem, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft });

    // The "Group" submenu of a folder's menu: which group it is in, as a radio
    // set, and a way to make a new one for it.
    //
    //   ○ No group
    //   ● Work
    //   ─────────
    //     New group…
    private MenuFlyoutSubItem BuildGroupSubMenu(string folderPath)
    {
        var sub = new MenuFlyoutSubItem
        {
            Text = FoldersText.Get("Folders_GroupMenu"),
            Icon = new FontIcon { Glyph = GroupGlyph },
        };
        AutomationProperties.SetAutomationId(sub, GroupSubMenuId);
        const string radioSet = "folder-group";
        string? current = _folders.Find(folderPath)?.GroupId;
        bool inKnownGroup = current is not null && _folders.Groups.Any(g => g.Id == current);

        RadioMenuFlyoutItem Choice(string text, string? groupId, bool isChecked)
        {
            var radio = new RadioMenuFlyoutItem { Text = text, GroupName = radioSet, IsChecked = isChecked };
            AutomationProperties.SetAutomationId(radio, GroupChoiceId(groupId));
            radio.Click += (_, _) => MoveFolderToGroup(folderPath, groupId);
            return radio;
        }

        sub.Items.Add(Choice(FoldersText.Get("Folders_NoGroup"), null, !inKnownGroup));
        foreach (FolderGroup group in _folders.Groups)
            sub.Items.Add(Choice(group.Name, group.Id, inKnownGroup && group.Id == current));
        sub.Items.Add(new MenuFlyoutSeparator());
        var newGroup = new MenuFlyoutItem { Text = FoldersText.Get("Folders_NewGroup") };
        AutomationProperties.SetAutomationId(newGroup, NewGroupChoiceId);
        newGroup.Click += (_, _) => _ = NewGroupAsync(folderPath);
        sub.Items.Add(newGroup);
        return sub;
    }

    // The menu of a group, the same under "..." and at a right click. Removing
    // asks nothing: its folders stay added, in no group, so nothing is lost.
    //
    //   Rename group…
    //   ─────────
    //   Remove group    tooltip "Its folders stay in Deckle Folders"
    private MenuFlyout BuildGroupMenu(GroupItem parts)
    {
        string groupId = parts.Group.Id;
        var menu = new MenuFlyout();
        MenuFlyoutItem rename = MenuItem(FoldersText.Get("Folders_RenameGroup"), RenameGroupGlyph,
            () => _ = RenameGroupAsync(groupId));
        AutomationProperties.SetAutomationId(rename, RenameGroupEntryId);
        menu.Items.Add(rename);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem remove = MenuItem(FoldersText.Get("Folders_RemoveGroup"), RemoveGlyph, () => RemoveGroup(groupId));
        AutomationProperties.SetAutomationId(remove, RemoveGroupEntryId);
        string hint = FoldersText.Get("Folders_RemoveGroupHint");
        ToolTipService.SetToolTip(remove, hint);
        AutomationProperties.SetHelpText(remove, hint);
        menu.Items.Add(remove);
        return menu;
    }

    private void ShowGroupMenu(GroupItem parts, FrameworkElement anchor, Windows.Foundation.Point? at)
    {
        if (!_folders.Groups.Any(g => g.Id == parts.Group.Id)) return;
        MenuFlyout menu = BuildGroupMenu(parts);
        if (_reviewMenusInWindow)
        {
            ShowReviewMenu(menu, anchor, FlyoutPlacementMode.BottomEdgeAlignedRight);
            return;
        }
        if (at is { } position) menu.ShowAt(anchor, new FlyoutShowOptions { Position = position });
        else menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }

    // ── What the menus do ────────────────────────────────────────────────

    private void MoveFolderToGroup(string folderPath, string? groupId)
    {
        if (_folders.MoveToGroup(folderPath, groupId) is null) return;
        SyncFolderItems();
    }

    // Asks for a name; on Create makes the group, last of the groups, and, for
    // the "New group…" of a folder's menu, moves that folder into it.
    private async Task NewGroupAsync(string? moveFolderPath)
    {
        if (Content?.XamlRoot is not { } root) return;
        string? name = await FolderGroupDialog.AskAsync(root, null, n => _folders.IsGroupNameTaken(n));
        if (name is not null) CreateGroup(name, moveFolderPath);
    }

    private FolderGroup CreateGroup(string name, string? moveFolderPath)
    {
        FolderGroup group = _folders.AddGroup(name);
        if (moveFolderPath is not null) _folders.MoveToGroup(moveFolderPath, group.Id);
        SyncFolderItems();
        return group;
    }

    private async Task RenameGroupAsync(string groupId)
    {
        if (Content?.XamlRoot is not { } root) return;
        FolderGroup? group = _folders.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;
        string? name = await FolderGroupDialog.AskAsync(root, group.Name, n => _folders.IsGroupNameTaken(n, groupId));
        if (name is null || _folders.RenameGroup(groupId, name) is null) return;
        SyncFolderItems();
    }

    private void RemoveGroup(string groupId)
    {
        if (!_folders.RemoveGroup(groupId)) return;
        _collapsedGroups.Remove(groupId);
        SyncFolderItems();
    }

    // ── What the self-check drives ───────────────────────────────────────

    // Shortcuts to a state the walk then photographs: what the dialog's
    // Create does, and what the radio item of a folder's menu does. The
    // gestures themselves (a click that folds a group, its menu, the "Group"
    // submenu) are pressed through their controls (FoldersWindow.Gestures).
    public string CreateGroupForReview(string name) => CreateGroup(name, null).Id;

    public bool MoveFolderToGroupForReview(string path, string? groupId)
    {
        string normalized = FolderPaths.Normalize(path);
        MoveFolderToGroup(normalized, groupId);
        return _folders.Find(normalized)?.GroupId == groupId;
    }

    // The group a folder is in, as the store keeps it; null in no group.
    public string? FolderGroupIdForReview(string path) => _folders.Find(FolderPaths.Normalize(path))?.GroupId;

    public bool IsGroupExpandedForReview(string groupId) =>
        _groupItems.FirstOrDefault(g => g.Group.Id == groupId)?.Item.IsExpanded == true;

    // What a screen reader and the hover say of a group's item.
    public string? GroupTooltipForReview(string groupId) =>
        _groupItems.FirstOrDefault(g => g.Group.Id == groupId) is { } parts
            ? ToolTipService.GetToolTip(parts.Item) as string
            : null;

    // Null when the items of the pane stand in the order FolderGrouping gives
    // (the folders in no group, then each group with its folders), the first
    // difference otherwise. Read off the pane's own lists, not off the window's
    // bookkeeping.
    public string? PaneOrderMismatchForReview()
    {
        List<string> actual = [];
        foreach (object top in Nav.MenuItems.Skip(PaneLeadingItems))
        {
            if (top is not NavigationViewItem { Tag: { } tag } item) continue;
            if (tag is AddedFolder folder) actual.Add(folder.Path);
            else if (tag is FolderGroup group)
            {
                actual.Add("group " + group.Name);
                actual.AddRange(item.MenuItems.OfType<NavigationViewItem>().Select(i => ((AddedFolder)i.Tag).Path));
            }
        }

        FolderGrouping grouping = Grouping;
        List<string> expected =
        [
            .. grouping.Ungrouped.Select(f => f.Path),
            .. grouping.Groups.SelectMany(g => g.Folders.Select(f => f.Path).Prepend("group " + g.Group.Name)),
        ];
        if (actual.SequenceEqual(expected, FolderPaths.Comparer)) return null;
        return $"The pane holds [{string.Join(" | ", actual)}], the grouping gives [{string.Join(" | ", expected)}].";
    }

    // The "Add…" menu as a click on the footer item opens it, kept inside the
    // window with the solid fill the review menus take (ShowFolderMenuForReview).
    public IReadOnlyList<string> ShowAddMenuForReview()
    {
        _reviewMenu?.Hide();
        MenuFlyout menu = BuildAddMenu();
        ShowReviewMenu(menu, AddItem, FlyoutPlacementMode.TopEdgeAlignedLeft);
        return MenuLabels(menu);
    }

    private ContentDialog? _reviewDialog;

    // The group dialog as "New group…" opens it, with a name typed in its
    // field. Returns once it is open; true when Create is enabled.
    public async Task<bool> ShowGroupDialogForReviewAsync(string typedName)
    {
        if (Content?.XamlRoot is not { } root) return false;
        var opened = new TaskCompletionSource<ContentDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = FolderGroupDialog.AskAsync(root, null, n => _folders.IsGroupNameTaken(n), opened.SetResult);
        ContentDialog dialog = await opened.Task;
        var field = (TextBox)((StackPanel)dialog.Content).Children[0];
        // Opened can come before the field's template is loaded; a text set before
        // then raises no TextChanged, so the dialog never hears it, which a person
        // typing in a loaded field does not meet.
        for (int loading = 0; !field.IsLoaded && loading < 2000; loading += 50) await Task.Delay(50);
        field.Text = typedName;
        _reviewDialog = dialog;
        // The field's change reaches the dialog's button on the dispatcher, a
        // moment after the text is set.
        for (int waited = 0; !dialog.IsPrimaryButtonEnabled && waited < 2000; waited += 50) await Task.Delay(50);
        return dialog.IsPrimaryButtonEnabled;
    }

    public void CloseGroupDialogForReview()
    {
        _reviewDialog?.Hide();
        _reviewDialog = null;
    }

    // One item of the pane for a group. The chevron is what the rail draws of
    // it; the opened pane has no icon for a group, so the first column folds
    // away and the name starts where the folders' icons do.
    private sealed class GroupItem(
        NavigationViewItem item, TextBlock name, Grid content, FrameworkElement chevron, Button more)
        : PaneItem(item, more)
    {
        // The group shown; a spare item keeps its last one.
        public FolderGroup Group { get; set; } = null!;

        public void Show(GroupedFolders grouped)
        {
            Group = grouped.Group;
            Item.Tag = grouped.Group;
            name.Text = grouped.Group.Name;

            string label = grouped.Folders.Count == 0
                ? FoldersText.Format("Folders_Group_Empty_Format", grouped.Group.Name)
                : FoldersText.Format("Folders_Group_Format", grouped.Group.Name,
                    string.Join(", ", grouped.Folders.Select(f => f.Name)));
            if (Equals(ToolTipService.GetToolTip(Item), label)) return;
            ToolTipService.SetToolTip(Item, label);
            AutomationProperties.SetName(Item, label);
        }

        public override void SetPaneOpen(bool open)
        {
            base.SetPaneOpen(open);
            chevron.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            content.ColumnSpacing = open ? 0 : PaneColumnSpacing;
        }
    }
}
