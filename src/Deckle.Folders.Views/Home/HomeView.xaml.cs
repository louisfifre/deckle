using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// The Home page: a tile for each added folder. It only shows the folders it
// is given and says which one a person opened; the window owns where it goes.
internal sealed partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    // The tile a click, Enter or Space invoked.
    public event Action<AddedFolder>? FolderOpened;

    // Built again from what the store holds each time, so the tiles follow
    // the pane.
    public void Show(IReadOnlyList<AddedFolder> folders) => Tiles.ItemsSource = folders.ToList();

    private void OnTileInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AddedFolder folder) FolderOpened?.Invoke(folder);
    }
}
