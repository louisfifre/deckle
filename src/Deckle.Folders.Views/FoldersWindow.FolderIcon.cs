using Deckle.Catalog;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// Each added folder's icon: drawn in the pane, in the address bar and on its
// Home tile, changed with the picker the address bar opens. A choice is saved
// the moment it is made, and the places redraw at once.
public sealed partial class FoldersWindow
{
    // The side of a folder's icon in the pane: the symbol alone is 16 px, the
    // size of the pane's other icons.
    private const double PaneIconBoxSize = 20;

    // The pane item draws the icon in its content, not in NavigationViewItem.
    // Icon: that slot takes an IconElement, a single glyph, and cannot hold
    // the square the background option puts behind the symbol. The content's
    // first column sits where the slot's icon would, so reduced to the rail
    // the item shows the icon alone, and opened it shows the icon then the
    // name.
    private static Grid FolderItemContent(out IconChoiceView icon, UIElement name)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        icon = new IconChoiceView { BoxSize = PaneIconBoxSize, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(icon);
        Grid.SetColumn((FrameworkElement)name, 1);
        grid.Children.Add(name);
        return grid;
    }

    private void OnFolderIconChosen(string rootPath, IconChoice choice)
    {
        if (_folders.ChangeIcon(rootPath, choice) is not { } changed) return;
        SyncFolderItems();
        Address.ShowIcon(changed);
    }

    // What the self-check drives: the picker opened as a click on the icon
    // opens it, and a choice made as a tile of the picker makes it.
    public bool OpenIconPickerForReview()
    {
        Address.OpenIconPicker();
        return Address.IsIconPickerOpen;
    }

    public void CloseIconPickerForReview() => Address.CloseIconPicker();

    public void ChangeFolderIconForReview(string rootPath, IconChoice choice) => OnFolderIconChosen(rootPath, choice);
}
