using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// One block of tiles on Home: the folders in no group have no header, a group
// has its name.
internal sealed record HomeSection(string? Header, IReadOnlyList<AddedFolder> Folders)
{
    public Visibility HeaderVisibility => Header is null ? Visibility.Collapsed : Visibility.Visible;
}

// The Home page: a tile for each added folder, in the sections the pane shows
// them in. It only shows the folders it is given and says which one a person
// opened; the window owns where it goes.
internal sealed partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    // The tile a click, Enter or Space invoked.
    public event Action<AddedFolder>? FolderOpened;

    // Built again from what the store holds each time, so the tiles follow
    // the pane. A group without folders shows nothing here: it has no tile to
    // draw, and the pane is where it is renamed or removed.
    public void Show(FolderGrouping grouping)
    {
        List<HomeSection> sections = [];
        if (grouping.Ungrouped.Count > 0) sections.Add(new HomeSection(null, grouping.Ungrouped));
        sections.AddRange(grouping.Groups
            .Where(g => g.Folders.Count > 0)
            .Select(g => new HomeSection(g.Group.Name, g.Folders)));
        Sections.ItemsSource = sections;
    }

    private void OnTileInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AddedFolder folder) FolderOpened?.Invoke(folder);
    }
}
