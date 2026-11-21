using Deckle.Catalog;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// The added folder's icon at the left of the address bar, and the picker it
// opens (spec, Window; 12.3, 12.3b). The bar shows the icon of the added
// folder the place sits in, whatever the depth, and a click opens the shared
// picker under it. Each choice is reported at once; the window saves it and
// redraws the pane and the bar.
internal sealed partial class AddressBar
{
    // The side of the icon in the bar: the symbol alone is 16 px, as File
    // Explorer's address bar icon.
    private const double IconBoxSize = 20;

    private Flyout? _picker;

    // The path of the added folder whose icon was chosen, and the choice.
    public event Action<string, IconChoice>? IconChosen;

    public bool IsIconPickerOpen => _picker?.IsOpen == true;

    // Draws the icon of this added folder as the store now holds it: the place
    // keeps the folder as it was when the place was made.
    public void ShowIcon(AddedFolder root)
    {
        AddedFolder current = Folders?.Find(root.Path) ?? root;
        RootIcon.BoxSize = IconBoxSize;
        RootIcon.Choice = current.Icon;
        string label = FoldersText.Format("Address_Icon_Format", current.Name);
        ToolTipService.SetToolTip(IconButton, label);
        AutomationProperties.SetName(IconButton, label);
    }

    public void OpenIconPicker()
    {
        if (_place is null || IsIconPickerOpen) return;
        LeaveTextMode();
        string rootPath = _place.Root.Path;
        AddedFolder root = Folders?.Find(rootPath) ?? _place.Root;
        _picker = IconPicker.CreateFlyout(root.Icon, FoldersText.Get, choice => IconChosen?.Invoke(rootPath, choice));
        _picker.Closed += (_, _) => _picker = null;
        _picker.ShowAt(IconButton);
    }

    public void CloseIconPicker() => _picker?.Hide();

    private void OnIconClick(object sender, RoutedEventArgs e) => OpenIconPicker();
}
