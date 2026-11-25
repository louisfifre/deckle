using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Deckle.Core;

namespace Deckle.Folders.Views;

// The added folders: the pane that lists them, the ways to add one, the menu
// of one, and what the window shows of a folder that is no longer there.
public sealed partial class FoldersWindow
{
    // Segoe Fluent Icons code points of the item's menu.
    private const string MoreGlyph = "";           // More, the "..."
    private const string ShowInExplorerGlyph = ""; // FolderOpen
    private const string ChangeIconGlyph = "";     // Edit, the pencil
    private const string RemoveGlyph = "";         // Remove, the minus

    // The pane's items before the added folders: "Home", then its separator
    // (FoldersWindow.xaml). The folders in no group follow them, then one item
    // per group, each holding its folders.
    private const int PaneLeadingItems = 2;

    // The "..." button's slot at the end of an item, the same on every item,
    // shown or not, so no name shifts when the button appears.
    private const double MoreSlotWidth = 32;


    private readonly AddedFolderStore _folders;

    // Every folder item and group item that is in the pane, in no order: where
    // each one sits is PaneItem.Host and its place in that list.
    private readonly List<FolderItem> _folderItems = [];
    private readonly List<GroupItem> _groupItems = [];

    // The items of removed folders and groups, kept for the next ones added.
    // An item is never let go: NavigationView still tells an item taken out of
    // MenuItems that the pane opens, and once the garbage collector has
    // taken that item, setting IsPaneOpen throws E_FAIL (FolderPaneBugNotes.md).
    // That holds for a group item as for a folder item, and for an item that
    // moves between lists: _folderItems and _groupItems hold it, not the list.
    private readonly Stack<FolderItem> _spareItems = new();
    private readonly Stack<GroupItem> _spareGroupItems = new();

    // What the last check said of each added folder, by path: the pane marks
    // a folder that vanished or cannot be read (spec, Open 33).
    private readonly Dictionary<string, AddedFolderReachability> _reach = new(FolderPaths.Comparer);
    private Task _reachChecked = Task.CompletedTask;
    private int _reachRequest;
    private bool _paneHooked;

    // The added folder whose message the centre shows in place of the tool,
    // or null.
    private string? _unreachableShown;

    // What the store holds, in the order the pane and Home show it.
    private FolderGrouping Grouping => FolderGrouping.Of(_folders.Folders, _folders.Groups);

    // One item per added folder, after Home and its separator: the folders in
    // no group first, then each group as an item with its folders under it.
    // Reduced to the icon, an item says its name and path on hover and to a
    // screen reader; opened, it shows the icon and the name on one line
    // (boards 24/11, A2; the path stays in the tooltip), and the "..." button
    // at its end while the pointer is over it or the keyboard is on it. The
    // icon is the folder's own, in its colour, drawn by the item's content
    // (FoldersWindow.FolderIcon.cs).
    //
    // The pane is brought to what the store holds without building it
    // again: an item whose folder is still there is updated where it is, or
    // moved to where the store puts it (taken out of the list that holds it,
    // put in the right one at the right index), a folder that has none takes a
    // spare item or a new one, and an item whose folder went becomes a spare
    // (_spareItems). Groups go the same way (_spareGroupItems). The pane is
    // never cleared.
    private void SyncFolderItems()
    {
        HookPaneInputOnce();
        FolderGrouping grouping = Grouping;
        IReadOnlyList<AddedFolder> folders = _folders.Folders;

        for (int i = _folderItems.Count - 1; i >= 0; i--)
        {
            FolderItem parts = _folderItems[i];
            if (folders.Any(f => FolderPaths.Comparer.Equals(f.Path, parts.Folder.Path))) continue;
            Detach(parts);
            _folderItems.RemoveAt(i);
            parts.Rest();
            _spareItems.Push(parts);
        }

        // A group that went lets its folders go first, so none is left under
        // an item that is no longer in the pane; they are put back below.
        for (int i = _groupItems.Count - 1; i >= 0; i--)
        {
            GroupItem group = _groupItems[i];
            if (grouping.Groups.Any(g => g.Group.Id == group.Group.Id)) continue;
            foreach (FolderItem parts in _folderItems.Where(p => ReferenceEquals(p.Host, group)).ToList()) Detach(parts);
            Detach(group);
            _groupItems.RemoveAt(i);
            group.Rest();
            _spareGroupItems.Push(group);
        }

        int at = PaneLeadingItems;
        foreach (AddedFolder folder in grouping.Ungrouped) Put(FolderItemFor(folder), null, at++);

        foreach (GroupedFolders grouped in grouping.Groups)
        {
            GroupItem group = GroupItemFor(grouped.Group);
            Put(group, null, at++);
            int inside = 0;
            foreach (AddedFolder folder in grouped.Folders) Put(FolderItemFor(folder), group, inside++);
            group.Show(grouped);
        }

        if (Place is { } place) SelectFolderItem(place.Root);
        // The tiles of Home follow the store as the pane does.
        else if (AtHome) ShowHome();
        _reachChecked = CheckFolderReachAsync();
    }

    // The item that shows this folder: the one it has, else a spare or a new
    // one. Shown again each time, which updates it where it is.
    private FolderItem FolderItemFor(AddedFolder folder)
    {
        FolderItem? parts = _folderItems.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Folder.Path, folder.Path));
        if (parts is null)
        {
            parts = _spareItems.Count > 0 ? _spareItems.Pop() : NewFolderItem();
            _folderItems.Add(parts);
        }
        parts.Show(folder);
        parts.SetPaneOpen(Nav.IsPaneOpen);
        MarkReach(parts, _reach.GetValueOrDefault(folder.Path, AddedFolderReachability.Reachable));
        return parts;
    }

    // The item that shows this group; one that is new or comes back from the
    // spares starts expanded unless the person collapsed this group.
    private GroupItem GroupItemFor(FolderGroup group)
    {
        GroupItem? item = _groupItems.FirstOrDefault(g => g.Group.Id == group.Id);
        if (item is null)
        {
            item = _spareGroupItems.Count > 0 ? _spareGroupItems.Pop() : NewGroupItem();
            _groupItems.Add(item);
            item.Group = group;
            item.Item.IsExpanded = !_collapsedGroups.Contains(group.Id);
        }
        item.Group = group;
        item.SetPaneOpen(Nav.IsPaneOpen);
        return item;
    }

    // Puts an item at this index of the list that holds its place: the pane's
    // own, or the group's. An item already there stays; one elsewhere is
    // taken out of the list that held it first. Everything before the index
    // is already right, so the index is still the one after the removal.
    private void Put(PaneItem item, GroupItem? host, int index)
    {
        IList<object> list = ListOf(host);
        if (item.Placed && ReferenceEquals(item.Host, host) && index < list.Count
            && ReferenceEquals(list[index], item.Item)) return;
        Detach(item);
        list.Insert(index, item.Item);
        item.Host = host;
        item.Placed = true;
    }

    private void Detach(PaneItem item)
    {
        if (!item.Placed) return;
        if (ReferenceEquals(Nav.SelectedItem, item.Item)) Nav.SelectedItem = null;
        ListOf(item.Host).Remove(item.Item);
        item.Host = null;
        item.Placed = false;
    }

    private IList<object> ListOf(GroupItem? host) => host is null ? Nav.MenuItems : host.Item.MenuItems;

    // One item and what it shows, empty until Show gives it a folder. The
    // pointer and the focus are heard by the pane for all its items
    // (HookPaneInputOnce); the item itself carries the button's click and its
    // context request.
    private FolderItem NewFolderItem()
    {
        // One line, trimmed: in the 48 px rail a wrapping name breaks at every
        // letter and stretches the item to the window's height.
        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Button more = MoreButton();
        Grid content = FolderItemContent(out IconChoiceView icon, name);
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MoreSlotWidth) });
        Grid.SetColumn(more, 2);
        content.Children.Add(more);

        var item = new NavigationViewItem
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var parts = new FolderItem(item, name, icon, more);
        more.Click += (_, _) => ShowFolderMenu(parts, more, null);
        item.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            // A right click opens the menu where the pointer is, in the rail
            // as in the opened pane; the menu key and Shift+F10 give no
            // position, and the menu opens under the item.
            if (args.TryGetPosition(item, out Windows.Foundation.Point at)) ShowFolderMenu(parts, item, at);
            else ShowFolderMenu(parts, item, null);
        };
        return parts;
    }

    // "...": the item's only button, hidden until the pointer or the keyboard
    // is on the item, as a project card's "..." is.
    private static Button MoreButton()
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = MoreGlyph, FontSize = 14 },
            Opacity = 0,
        };
        string label = FoldersText.Get("Folders_More");
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    // The menu of an added folder, the same under "..." and at a right click
    // (Louis, 2026-11-21: the cross became "...", and the right click works
    // on the reduced pane too, where "..." has no room). Built each time it
    // opens, for the folder the item shows then.
    //
    //   Open in File Explorer
    //   Change icon…
    //   Group                         ▸ No group / each group / New group…
    //   ─────────
    //   Remove from Deckle Folders    tooltip "The folder stays on the disk"
    //
    // There is no "Open": a click on the item does that, as on a project card.
    // Removing asks nothing: nothing on the disk changes, and adding the
    // folder again undoes it.
    private MenuFlyout BuildFolderMenu(FolderItem parts)
    {
        string folderPath = parts.Folder.Path;
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(FoldersText.Get("Folders_ShowInExplorer"), ShowInExplorerGlyph,
            () => _ = OpenFolderInExplorerAsync(folderPath)));
        menu.Items.Add(MenuItem(FoldersText.Get("Folders_ChangeIcon"), ChangeIconGlyph,
            () => OpenFolderIconPicker(parts)));
        menu.Items.Add(BuildGroupSubMenu(folderPath));
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem remove = MenuItem(FoldersText.Get("Folders_Remove"), RemoveGlyph,
            () => _ = RemoveFolderAsync(folderPath));
        string hint = FoldersText.Get("Folders_RemoveHint");
        ToolTipService.SetToolTip(remove, hint);
        AutomationProperties.SetHelpText(remove, hint);
        menu.Items.Add(remove);
        return menu;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action invoke)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => invoke();
        return item;
    }

    private void ShowFolderMenu(FolderItem parts, FrameworkElement anchor, Windows.Foundation.Point? at)
    {
        if (_folders.Find(parts.Folder.Path) is null) return;
        MenuFlyout menu = BuildFolderMenu(parts);
        if (_reviewMenusInWindow)
        {
            ShowReviewMenu(menu, anchor, FlyoutPlacementMode.BottomEdgeAlignedRight);
            return;
        }
        if (at is { } position) menu.ShowAt(anchor, new FlyoutShowOptions { Position = position });
        else
        {
            // Under the anchor and flush with its right edge, as a project
            // card's menu opens; the system flips it above when there is no
            // room below.
            menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
        }
    }

    // File Explorer opens on the added folder itself, not on its parent. The
    // shell blocks, so it runs off the UI thread; a folder found missing
    // takes its mark in the pane.
    private async Task OpenFolderInExplorerAsync(string folderPath)
    {
        LaunchOutcome outcome = await Task.Run(() => new ShellFileLauncher().Open(folderPath));
        if (outcome.Status == LaunchStatus.Missing) NoteReach(folderPath, AddedFolderReachability.NotFound);
    }

    // The icon picker opened from the menu, beside the item's own icon; the
    // choice applies at once, as from the address bar.
    private void OpenFolderIconPicker(FolderItem parts)
    {
        string rootPath = parts.Folder.Path;
        if (_folders.Find(rootPath) is not { } folder) return;
        Flyout picker = IconPicker.CreateFlyout(folder.Icon, FoldersText.Get, choice => OnFolderIconChosen(rootPath, choice));
        picker.Placement = FlyoutPlacementMode.RightEdgeAlignedTop;
        picker.ShowAt(parts.Item);
    }

    // The pane's input, heard once for all its items. The pointer over an
    // item, or the keyboard on it or on its button, shows the button; the
    // pane opening shows the slots and closing hides them, after
    // NavigationView's own change is done. Hooked from the first rebuild,
    // which the constructor runs once the controls exist.
    private void HookPaneInputOnce()
    {
        if (_paneHooked) return;
        _paneHooked = true;
        FocusManager.GotFocus += OnFocusMoved;
        Closed += (_, _) => FocusManager.GotFocus -= OnFocusMoved;
        // Handled too: an item handles the pointer it is pressed with.
        Nav.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPanePointerMoved), true);
        Nav.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPanePointerLeft), true);
        Nav.PaneOpening += (_, _) => OnPaneTransition();
        Nav.PaneClosing += (_, _) => OnPaneTransition();
        Nav.Expanding += (_, args) => OnGroupExpansionChanged(args.ExpandingItemContainer, true);
        Nav.Collapsed += (_, args) => OnGroupExpansionChanged(args.CollapsedItemContainer, false);
    }

    // NavigationView's own change is done once the dispatcher gets to this.
    private void OnPaneTransition()
    {
        _paneTransition = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            SetItemsPaneOpen(Nav.IsPaneOpen);
            _paneTransition = false;
        });
    }

    private void SetItemsPaneOpen(bool open)
    {
        foreach (PaneItem item in PaneItems) item.SetPaneOpen(open);
        // NavigationView folds the groups as the rail takes the pane; the
        // opened pane shows each as the person left it.
        if (open)
            foreach (GroupItem group in _groupItems) group.Item.IsExpanded = !_collapsedGroups.Contains(group.Group.Id);
    }

    private IEnumerable<PaneItem> PaneItems => _groupItems.Cast<PaneItem>().Concat(_folderItems);

    private void OnPanePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Windows.Foundation.Point at = e.GetCurrentPoint(null).Position;
        foreach (PaneItem item in PaneItems)
            item.SetPointerOver(Nav.IsPaneOpen && Covers(item.Region, at));
    }

    private void OnPanePointerLeft(object sender, PointerRoutedEventArgs e)
    {
        foreach (PaneItem item in PaneItems) item.SetPointerOver(false);
    }

    private static bool Covers(FrameworkElement element, Windows.Foundation.Point at)
    {
        if (element.XamlRoot is null || element.ActualWidth == 0) return false;
        Windows.Foundation.Rect bounds = element.TransformToVisual(null)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Contains(at);
    }

    private void OnFocusMoved(object? sender, FocusManagerGotFocusEventArgs args)
    {
        if (args.NewFocusedElement is not UIElement focused || !ReferenceEquals(focused.XamlRoot, Content?.XamlRoot)) return;
        bool keyboard = focused.FocusState == FocusState.Keyboard;
        foreach (PaneItem item in PaneItems)
            item.SetKeyboardFocus(keyboard && IsInside(focused, item.Region));
    }


    private void SelectHomeItem() => Nav.SelectedItem = HomeItem;

    // The item of the folder, wherever it sits: at the top of the pane or
    // under a group. Under a collapsed group NavigationView marks the group.
    private void SelectFolderItem(AddedFolder root)
    {
        FolderItem? parts = _folderItems.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Folder.Path, root.Path));
        if (parts is not null) Nav.SelectedItem = parts.Item;
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ReferenceEquals(args.InvokedItemContainer, AddItem))
        {
            ShowAddMenu();
            return;
        }
        if (ReferenceEquals(args.InvokedItemContainer, HomeItem))
        {
            _pane.CloseIfOverPage();
            NavigateHome();
            return;
        }
        // A folder its remove button just forgot is not opened.
        if (args.InvokedItemContainer is NavigationViewItem { Tag: AddedFolder folder }
            && _folders.Find(folder.Path) is not null)
        {
            _pane.CloseIfOverPage();
            _ = NavigateTo(FolderPlace.AtRoot(folder));
        }
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e) => _ = PickAndAddFolderAsync();

    private async Task PickAndAddFolderAsync()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
        var picked = await picker.PickSingleFolderAsync();
        if (picked is null || string.IsNullOrEmpty(picked.Path)) return;
        _pane.CloseIfOverPage();
        await AddAndOpen(picked.Path);
    }

    // A folder just added appears last in the pane and opens in the file tool,
    // nothing selected and no panel (12.20): it has no tool yet, so it opens in
    // Files. Adding a known folder opens it, in the tool it was left in.
    private Task AddAndOpen(string path)
    {
        AddedFolder folder = _folders.Add(path);
        SyncFolderItems();
        return NavigateTo(FolderPlace.AtRoot(folder));
    }

    // Forgets the added folder, never touches the disk. Back and Forward lose
    // every place inside it. Standing in it, the window moves to the folder
    // that takes its place in the pane, or the one before when it was last;
    // with none left, it goes Home, which shows the empty window (12.11).
    // Home is never forgotten.
    private Task RemoveFolderAsync(string path)
    {
        int index = Grouping.InPaneOrder.ToList().FindIndex(f => FolderPaths.Comparer.Equals(f.Path, path));
        if (index < 0 || !_folders.Remove(path)) return Task.CompletedTask;
        _reach.Remove(path);
        bool standingInIt = _history.Forget(d =>
            d is FolderDestination folder && FolderPaths.Comparer.Equals(folder.Place.Root.Path, path));
        SyncFolderItems();

        List<AddedFolder> inPaneOrder = [.. Grouping.InPaneOrder];
        if (inPaneOrder.Count == 0)
        {
            // Standing in the folder, the history has no current place now and
            // Home takes it; standing at Home, Home shows again, empty.
            _history.Navigate(HomeDestination.Instance);
            _selection = null;
            ShowHome();
            return Task.CompletedTask;
        }
        if (standingInIt)
        {
            AddedFolder neighbour = inPaneOrder[Math.Min(index, inPaneOrder.Count - 1)];
            FocusFolderItem(neighbour);
            return NavigateTo(FolderPlace.AtRoot(neighbour));
        }

        FocusFolderItem(inPaneOrder[Math.Min(index, inPaneOrder.Count - 1)]);
        if (Place is { } place) SelectFolderItem(place.Root);
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
        return Task.CompletedTask;
    }

    // The removed item took the focus with it: the open pane hands it to the
    // neighbour, as a list does when one of its entries goes.
    private void FocusFolderItem(AddedFolder folder)
    {
        if (!Nav.IsPaneOpen) return;
        _folderItems.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Folder.Path, folder.Path))
            ?.Item.Focus(FocusState.Programmatic);
    }

    // ── A folder that is no longer there ─────────────────────────────────

    // Asks the disk about every added folder, off the UI thread, and marks
    // the items whose answer changed. A newer rebuild supersedes it.
    private async Task CheckFolderReachAsync()
    {
        int request = ++_reachRequest;
        string[] paths = [.. _folders.Folders.Select(f => f.Path)];
        AddedFolderReachability[] answers = await Task.Run(() => paths.Select(AddedFolderReach.Check).ToArray());
        if (request != _reachRequest) return;
        bool changed = false;
        for (int i = 0; i < paths.Length; i++) changed |= Remember(paths[i], answers[i]);
        if (changed) MarkFolderItems();
    }

    private void NoteReach(string path, AddedFolderReachability reach)
    {
        if (Remember(path, reach)) MarkFolderItems();
    }

    private bool Remember(string path, AddedFolderReachability reach)
    {
        AddedFolderReachability known = _reach.GetValueOrDefault(path, AddedFolderReachability.Reachable);
        _reach[path] = reach;
        return known != reach;
    }

    // Each item takes the mark of what the disk now says of its folder.
    private void MarkFolderItems()
    {
        foreach (FolderItem parts in _folderItems)
            MarkReach(parts, _reach.GetValueOrDefault(parts.Folder.Path, AddedFolderReachability.Reachable));
    }

    // A folder that vanished or cannot be read stays in the pane: Windows'
    // critical badge sits on it, in the rail as in the opened pane, and its
    // tooltip says what is wrong. The badge goes when the folder is back.
    private static void MarkReach(FolderItem parts, AddedFolderReachability reach)
    {
        string key = reach switch
        {
            AddedFolderReachability.NotFound => "Folders_Item_NotFound_Format",
            AddedFolderReachability.Unreadable => "Folders_Item_Unreadable_Format",
            _ => "Folders_Item_Format",
        };
        string label = FoldersText.Format(key, parts.Folder.Name, parts.Folder.Path);
        if (!Equals(ToolTipService.GetToolTip(parts.Item), label))
        {
            ToolTipService.SetToolTip(parts.Item, label);
            AutomationProperties.SetName(parts.Item, label);
        }

        bool marked = reach != AddedFolderReachability.Reachable;
        if (marked == parts.Item.InfoBadge is not null) return;
        parts.Item.InfoBadge = marked
            ? new InfoBadge { Style = (Style)Application.Current.Resources["CriticalIconInfoBadgeStyle"] }
            : null;
    }

    // In place of the tool: one message that names the path and offers to
    // remove the folder from the app. The top row stays usable (Back,
    // Forward, Refresh, the address bar, the pane); the tools have nothing
    // to look at and are disabled, as before any folder is added (12.11).
    private void ShowUnreachable(AddedFolder root, AddedFolderReachability reach)
    {
        _showing?.Cancel();
        _showingFolder = false;
        Select(null, null);
        SetToolButtonsEnabled(false);
        DeckleFoldersSource.Log.AddedFolderUnreachable(reach.ToString());

        bool notFound = reach == AddedFolderReachability.NotFound;
        var message = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(32),
            Spacing = 8,
            MaxWidth = 584,
        };
        message.Children.Add(new TextBlock
        {
            Text = FoldersText.Get(notFound ? "Folders_NotFound_Title" : "Folders_Unreadable_Title"),
            Style = (Style)Application.Current.Resources["BodyLargeTextBlockStyle"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        message.Children.Add(new TextBlock
        {
            Text = FoldersText.Format(notFound ? "Folders_NotFound_Body_Format" : "Folders_Unreadable_Body_Format", root.Path),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var remove = new Button
        {
            Content = FoldersText.Get("Folders_Remove"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        remove.Click += (_, _) => _ = RemoveFolderAsync(root.Path);
        message.Children.Add(remove);

        CentreHost.Content = new ScrollViewer { Content = message };
        _unreachableShown = root.Path;
    }

    private void SetToolButtonsEnabled(bool enabled)
    {
        foreach (UIElement button in ToolButtons.Children)
            if (button is Control control) control.IsEnabled = enabled;
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
            if (ReferenceEquals(e, ancestor)) return true;
        return false;
    }

    // ── Adding by drop ───────────────────────────────────────────────────

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Link;
    }

    private async void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        foreach (IStorageItem item in items)
        {
            if (item is StorageFolder folder && !string.IsNullOrEmpty(folder.Path))
                await AddAndOpen(folder.Path);
        }
    }

    // ── What the self-check drives ───────────────────────────────────────

    // The "..." button of this folder's item as the pointer over it shows it;
    // true when it is shown.
    public bool ShowMoreButtonForReview(string path)
    {
        if (FindFolderItem(path) is not { } parts) return false;
        parts.SetPointerOver(true);
        return parts.MoreShown;
    }

    public void HideMoreButtonsForReview()
    {
        foreach (PaneItem item in PaneItems) item.SetPointerOver(false);
    }

    // Opens this folder's menu under its "...", as a click does, kept inside
    // the window so a capture of the window sees it (a menu a person opens
    // may leave the window's edge, in a window of its own). Inside the window
    // the menu's acrylic has nothing to blur, so the review's menu takes the
    // solid fill acrylic falls back to, as a project card's review menu does.
    // Answers the menu's labels, separators left out.
    public IReadOnlyList<string> ShowFolderMenuForReview(string path)
    {
        if (FindFolderItem(path) is not { } parts) return [];
        _reviewMenu?.Hide();
        MenuFlyout menu = BuildFolderMenu(parts);
        ShowReviewMenu(menu, parts.Item, null);
        return MenuLabels(menu);
    }

    // The labels of a menu, separators left out, a submenu by its own label.
    private static IReadOnlyList<string> MenuLabels(MenuFlyout menu) =>
    [
        .. menu.Items.Select(i => i switch
        {
            MenuFlyoutItem item => item.Text,
            MenuFlyoutSubItem sub => sub.Text,
            _ => null,
        }).OfType<string>(),
    ];

    private void ShowReviewMenu(MenuFlyout menu, FrameworkElement anchor, FlyoutPlacementMode? placement)
    {
        menu.ShouldConstrainToRootBounds = true;
        menu.MenuFlyoutPresenterStyle = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"MenuFlyoutPresenter\" "
            + "BasedOn=\"{StaticResource DefaultMenuFlyoutPresenterStyle}\">"
            + "<Setter Property=\"Background\" Value=\"{ThemeResource SolidBackgroundFillColorQuarternaryBrush}\" /></Style>");
        // The placement is set on the flyout: options given to ShowAt let the menu out of the window.
        if (placement is { } mode) menu.Placement = mode;
        menu.ShowAt(anchor);
        _reviewMenu = menu;
    }

    public void HideFolderMenuForReview()
    {
        _reviewMenu?.Hide();
        _reviewMenu = null;
    }

    private MenuFlyout? _reviewMenu;

    public Task RemoveFolderForReviewAsync(string path) => RemoveFolderAsync(FolderPaths.Normalize(path));

    private FolderItem? FindFolderItem(string path) =>
        _folderItems.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Folder.Path, FolderPaths.Normalize(path)));

    // Waits for the pane's check of the added folders, so an image shows the
    // marks settled; true when this folder is marked as unreachable.
    public async Task<bool> IsMarkedUnreachableForReviewAsync(string path)
    {
        await _reachChecked;
        return _folderItems.Any(p => FolderPaths.Comparer.Equals(p.Folder.Path, FolderPaths.Normalize(path)) && p.Item.InfoBadge is not null);
    }

    public bool ShowsUnreachableForReview(string path) =>
        _unreachableShown is { } shown && FolderPaths.Comparer.Equals(shown, FolderPaths.Normalize(path));

    // One item of the pane and its "..." button. The button shows while the
    // pointer is over the item or the keyboard is on it, and only in the
    // opened pane; hidden, it keeps its slot transparent. A folder and a group
    // are two kinds of it.
    private abstract class PaneItem(NavigationViewItem item, Button more)
    {
        private bool _pointerOver;
        private bool _keyboardFocus;
        private bool _paneOpen;
        private FrameworkElement? _presenter;

        public NavigationViewItem Item { get; } = item;

        // Where it sits: the group whose list holds it, null at the top of the
        // pane; Placed says whether a list holds it at all.
        public GroupItem? Host { get; set; }
        public bool Placed { get; set; }

        // What the pointer and the keyboard are tested against: the item's own
        // row. A group item's bounds take in its folders, which are items of
        // their own, so the row is the template's presenter.
        public FrameworkElement Region => (_presenter ??= FindPresenter(Item)) ?? Item;

        public bool MoreShown => _paneOpen && more.Opacity > 0;

        // The "..." button itself, for the self-check to press it as a click does.
        public Button More => more;

        // Out of the pane: nothing of the pointer or the keyboard stays on it.
        public void Rest()
        {
            _pointerOver = false;
            _keyboardFocus = false;
            ShowMore();
        }

        public void SetPointerOver(bool over) { _pointerOver = over; ShowMore(); }
        public void SetKeyboardFocus(bool focus) { _keyboardFocus = focus; ShowMore(); }

        public virtual void SetPaneOpen(bool open)
        {
            _paneOpen = open;
            more.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ShowMore();
        }

        private void ShowMore() => more.Opacity = _pointerOver || _keyboardFocus ? 1 : 0;

        private static FrameworkElement? FindPresenter(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is NavigationViewItemPresenter presenter) return presenter;
                if (child is NavigationViewItem) continue;
                if (FindPresenter(child) is { } found) return found;
            }
            return null;
        }
    }

    // One item of the pane for an added folder.
    private sealed class FolderItem(
        NavigationViewItem item, TextBlock name, IconChoiceView icon, Button more) : PaneItem(item, more)
    {
        // The folder shown; a spare item keeps its last one until Show.
        public AddedFolder Folder { get; private set; } = null!;

        public void Show(AddedFolder folder)
        {
            Folder = folder;
            Item.Tag = folder;
            name.Text = folder.Name;
            icon.Choice = folder.Icon;
        }
    }
}
