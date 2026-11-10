using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Deckle.Atelier.Views;

// The added folders: the pane that lists them and the ways to add one.
public sealed partial class AtelierWindow
{
    private readonly AddedFolderStore _folders;

    // One item per added folder, in the order they were added. Reduced to
    // the icon, an item says its name and path on hover and to a screen
    // reader; opened, it shows them on two lines (12.2, Open 14).
    private void RebuildFolderItems()
    {
        Nav.MenuItems.Clear();
        foreach (AddedFolder folder in _folders.Folders)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // One line each, trimmed: CaptionTextBlockStyle wraps, and in the
            // 48 px rail a wrapping path breaks at every letter and stretches
            // the item to the window's height.
            text.Children.Add(new TextBlock
            {
                Text = folder.Name,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = folder.Path,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var item = new NavigationViewItem
            {
                Content = text,
                Icon = new FontIcon { Glyph = AtelierGlyphs.Folder },
                Tag = folder,
            };
            string label = AtelierText.Format("Folders_Item_Format", folder.Name, folder.Path);
            ToolTipService.SetToolTip(item, label);
            AutomationProperties.SetName(item, label);
            Nav.MenuItems.Add(item);
        }
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
        if (args.InvokedItemContainer is NavigationViewItem { Tag: AddedFolder folder })
        {
            Nav.IsPaneOpen = false;
            _ = NavigateTo(FolderPlace.AtRoot(folder));
        }
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e) => _ = PickAndAddFolderAsync();

    private async Task PickAndAddFolderAsync()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
        var picked = await picker.PickSingleFolderAsync();
        if (picked is null || string.IsNullOrEmpty(picked.Path)) return;
        Nav.IsPaneOpen = false;
        await AddAndOpen(picked.Path);
    }

    // A folder just added appears last in the pane and opens in the file tool,
    // nothing selected and no panel (12.20). Adding a known folder opens it.
    private Task AddAndOpen(string path)
    {
        AddedFolder folder = _folders.Add(path);
        RebuildFolderItems();
        _activeTool = _tools[0];
        MarkActiveTool();
        return NavigateTo(FolderPlace.AtRoot(folder));
    }

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
}
