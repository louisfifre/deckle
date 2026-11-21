using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Deckle.Atelier.Views;

// The added folders: the pane that lists them, the ways to add one, the
// button that removes one, and what the window shows of a folder that is no
// longer there.
public sealed partial class AtelierWindow
{
    // Segoe Fluent Icons "Cancel", the small cross Windows puts on an entry
    // that a list forgets without deleting anything.
    private const string RemoveGlyph = "";

    // The remove button's slot at the end of an item, the same on every
    // item, shown or not, so no name shifts when the button appears.
    private const double RemoveSlotWidth = 32;


    private readonly AddedFolderStore _folders;
    private readonly List<FolderItem> _folderItems = [];

    // The items of removed folders, kept for the next folder added. An item
    // is never let go: NavigationView still tells an item taken out of
    // MenuItems that the pane opens, and once the garbage collector has
    // taken that item, setting IsPaneOpen throws E_FAIL (FolderPaneBugNotes.md).
    private readonly Stack<FolderItem> _spareItems = new();

    // What the last check said of each added folder, by path: the pane marks
    // a folder that vanished or cannot be read (spec, Open 33).
    private readonly Dictionary<string, AddedFolderReachability> _reach = new(FolderPaths.Comparer);
    private Task _reachChecked = Task.CompletedTask;
    private int _reachRequest;
    private bool _paneHooked;

    // The added folder whose message the centre shows in place of the tool,
    // or null.
    private string? _unreachableShown;

    // One item per added folder, in the order they were added. Reduced to
    // the icon, an item says its name and path on hover and to a screen
    // reader; opened, it shows them on two lines (12.2, Open 14), and the
    // remove button at its end while the pointer is over it or the keyboard
    // is on it. The icon is the folder's own, in its colour (spec, Window),
    // drawn by the item's content (AtelierWindow.FolderIcon.cs).
    //
    // The pane is brought to what the store holds without building it
    // again: an item whose folder is still there is updated where it is, a
    // folder that has none takes a spare item or a new one, and an item
    // whose folder went becomes a spare (_spareItems).
    private void SyncFolderItems()
    {
        HookPaneInputOnce();
        IReadOnlyList<AddedFolder> folders = _folders.Folders;

        for (int i = _folderItems.Count - 1; i >= 0; i--)
        {
            FolderItem parts = _folderItems[i];
            if (folders.Any(f => FolderPaths.Comparer.Equals(f.Path, parts.Folder.Path))) continue;
            if (ReferenceEquals(Nav.SelectedItem, parts.Item)) Nav.SelectedItem = null;
            Nav.MenuItems.RemoveAt(i);
            _folderItems.RemoveAt(i);
            parts.Rest();
            _spareItems.Push(parts);
        }

        for (int i = 0; i < folders.Count; i++)
        {
            AddedFolder folder = folders[i];
            int at = _folderItems.FindIndex(p => FolderPaths.Comparer.Equals(p.Folder.Path, folder.Path));
            FolderItem parts;
            if (at == i)
            {
                parts = _folderItems[i];
            }
            else
            {
                if (at >= 0)
                {
                    parts = _folderItems[at];
                    _folderItems.RemoveAt(at);
                    Nav.MenuItems.RemoveAt(at);
                }
                else
                {
                    parts = _spareItems.Count > 0 ? _spareItems.Pop() : NewFolderItem();
                }
                _folderItems.Insert(i, parts);
                Nav.MenuItems.Insert(i, parts.Item);
            }
            parts.Show(folder);
            parts.SetPaneOpen(Nav.IsPaneOpen);
            MarkReach(parts, _reach.GetValueOrDefault(folder.Path, AddedFolderReachability.Reachable));
        }
        if (Place is { } place) SelectFolderItem(place.Root);
        _reachChecked = CheckFolderReachAsync();
    }

    // One item and what it shows, empty until Show gives it a folder. The
    // pointer and the focus are heard by the pane for all its items
    // (HookPaneInputOnce); the item itself carries the button's click only.
    private FolderItem NewFolderItem()
    {
        // One line each, trimmed: CaptionTextBlockStyle wraps, and in the
        // 48 px rail a wrapping path breaks at every letter and stretches
        // the item to the window's height.
        var name = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var path = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        text.Children.Add(path);

        Button remove = RemoveButton();
        Grid content = FolderItemContent(out IconChoiceView icon, text);
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RemoveSlotWidth) });
        Grid.SetColumn(remove, 2);
        content.Children.Add(remove);

        var item = new NavigationViewItem
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var parts = new FolderItem(item, name, path, icon, remove);
        remove.Click += (_, _) => _ = RemoveFolderAsync(parts.Folder.Path);
        return parts;
    }

    // "Remove from Deckle Folders": not drawn; the spec keeps the right-click
    // free ("never by a right-click", of the icon), so the removal is a
    // button in sight, as File Explorer's own lists put a cross on an entry.
    // No confirmation: nothing on the disk changes, and adding the folder
    // again undoes it.
    private static Button RemoveButton()
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = RemoveGlyph, FontSize = 12 },
            Opacity = 0,
        };
        string label = AtelierText.Get("Folders_Remove");
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
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
        Nav.PaneOpening += (_, _) => DispatcherQueue.TryEnqueue(() => SetItemsPaneOpen(Nav.IsPaneOpen));
        Nav.PaneClosing += (_, _) => DispatcherQueue.TryEnqueue(() => SetItemsPaneOpen(Nav.IsPaneOpen));
    }

    private void SetItemsPaneOpen(bool open)
    {
        foreach (FolderItem parts in _folderItems) parts.SetPaneOpen(open);
    }

    private void OnPanePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Windows.Foundation.Point at = e.GetCurrentPoint(null).Position;
        foreach (FolderItem parts in _folderItems)
            parts.SetPointerOver(Nav.IsPaneOpen && Covers(parts.Item, at));
    }

    private void OnPanePointerLeft(object sender, PointerRoutedEventArgs e)
    {
        foreach (FolderItem parts in _folderItems) parts.SetPointerOver(false);
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
        foreach (FolderItem parts in _folderItems)
            parts.SetKeyboardFocus(keyboard && IsInside(focused, parts.Item));
    }


    private void SelectFolderItem(AddedFolder root)
    {
        foreach (object item in Nav.MenuItems)
        {
            if (item is NavigationViewItem { Tag: AddedFolder folder } nvi
                && FolderPaths.Comparer.Equals(folder.Path, root.Path))
            {
                Nav.SelectedItem = nvi;
                return;
            }
        }
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ReferenceEquals(args.InvokedItemContainer, AddFolderItem))
        {
            _ = PickAndAddFolderAsync();
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
    // nothing selected and no panel (12.20). Adding a known folder opens it.
    private Task AddAndOpen(string path)
    {
        AddedFolder folder = _folders.Add(path);
        SyncFolderItems();
        _activeTool = _tools[0];
        MarkActiveTool();
        return NavigateTo(FolderPlace.AtRoot(folder));
    }

    // Forgets the added folder, never touches the disk. Back and Forward lose
    // every place inside it. Standing in it, the window moves to the folder
    // that takes its place in the pane, or the one above when it was last;
    // with none left, it shows the empty window (12.11).
    private Task RemoveFolderAsync(string path)
    {
        int index = _folders.Folders.ToList().FindIndex(f => FolderPaths.Comparer.Equals(f.Path, path));
        if (index < 0 || !_folders.Remove(path)) return Task.CompletedTask;
        _reach.Remove(path);
        bool standingInIt = _history.Forget(p => FolderPaths.Comparer.Equals(p.Root.Path, path));
        SyncFolderItems();

        if (_folders.Folders.Count == 0)
        {
            MarkActiveTool();
            ShowEmptyState();
            return Task.CompletedTask;
        }
        if (standingInIt)
        {
            AddedFolder neighbour = _folders.Folders[Math.Min(index, _folders.Folders.Count - 1)];
            FocusFolderItem(neighbour);
            return NavigateTo(FolderPlace.AtRoot(neighbour));
        }

        FocusFolderItem(_folders.Folders[Math.Min(index, _folders.Folders.Count - 1)]);
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
        string label = AtelierText.Format(key, parts.Folder.Name, parts.Folder.Path);
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
        Select(null, null);
        SetToolButtonsEnabled(false);
        DeckleAtelierSource.Log.AddedFolderUnreachable(reach.ToString());

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
            Text = AtelierText.Get(notFound ? "Folders_NotFound_Title" : "Folders_Unreadable_Title"),
            Style = (Style)Application.Current.Resources["BodyLargeTextBlockStyle"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        message.Children.Add(new TextBlock
        {
            Text = AtelierText.Format(notFound ? "Folders_NotFound_Body_Format" : "Folders_Unreadable_Body_Format", root.Path),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var remove = new Button
        {
            Content = AtelierText.Get("Folders_Remove"),
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

    // The remove button of this folder's item as the pointer over it shows
    // it; true when it is shown.
    public bool ShowRemoveButtonForReview(string path)
    {
        if (_folderItems.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Folder.Path, FolderPaths.Normalize(path))) is not { } parts)
            return false;
        parts.SetPointerOver(true);
        return parts.RemoveShown;
    }

    public void HideRemoveButtonsForReview()
    {
        foreach (FolderItem parts in _folderItems) parts.SetPointerOver(false);
    }

    public Task RemoveFolderForReviewAsync(string path) => RemoveFolderAsync(FolderPaths.Normalize(path));

    // Waits for the pane's check of the added folders, so an image shows the
    // marks settled; true when this folder is marked as unreachable.
    public async Task<bool> IsMarkedUnreachableForReviewAsync(string path)
    {
        await _reachChecked;
        return _folderItems.Any(p => FolderPaths.Comparer.Equals(p.Folder.Path, FolderPaths.Normalize(path)) && p.Item.InfoBadge is not null);
    }

    public bool ShowsUnreachableForReview(string path) =>
        _unreachableShown is { } shown && FolderPaths.Comparer.Equals(shown, FolderPaths.Normalize(path));

    // One item of the pane, the folder it shows and its remove button. The
    // button shows while the pointer is over the item or the keyboard is on
    // it, and only in the opened pane; hidden, it keeps its slot transparent.
    private sealed class FolderItem(
        NavigationViewItem item, TextBlock name, TextBlock path, IconChoiceView icon, Button remove)
    {
        private bool _pointerOver;
        private bool _keyboardFocus;
        private bool _paneOpen;

        public NavigationViewItem Item { get; } = item;

        // The folder shown; a spare item keeps its last one until Show.
        public AddedFolder Folder { get; private set; } = null!;

        public bool RemoveShown => _paneOpen && remove.Opacity > 0;

        public void Show(AddedFolder folder)
        {
            Folder = folder;
            Item.Tag = folder;
            name.Text = folder.Name;
            path.Text = folder.Path;
            icon.Choice = folder.Icon;
        }

        // Out of the pane: nothing of the pointer or the keyboard stays on it.
        public void Rest()
        {
            _pointerOver = false;
            _keyboardFocus = false;
            ShowRemove();
        }

        public void SetPointerOver(bool over) { _pointerOver = over; ShowRemove(); }
        public void SetKeyboardFocus(bool focus) { _keyboardFocus = focus; ShowRemove(); }

        public void SetPaneOpen(bool open)
        {
            _paneOpen = open;
            remove.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ShowRemove();
        }

        private void ShowRemove() => remove.Opacity = _pointerOver || _keyboardFocus ? 1 : 0;
    }
}
