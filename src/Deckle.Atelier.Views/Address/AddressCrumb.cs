using Microsoft.UI.Xaml;

namespace Deckle.Atelier.Views;

// One segment of the address bar as the BreadcrumbBar shows it: its name, the
// place a click leads to, and whether it has folders inside, which is when its
// chevron shows (File Explorer shows no chevron after a folder with none).
internal sealed class AddressCrumb
{
    public AddressCrumb(PathSegment segment, FolderPlace place, bool hasSubfolders)
    {
        Name = segment.Name;
        Place = place;
        ChevronVisibility = hasSubfolders ? Visibility.Visible : Visibility.Collapsed;
        ChevronLabel = AtelierText.Format("Address_Chevron_Format", segment.Name);
    }

    public string Name { get; }
    public FolderPlace Place { get; }
    public Visibility ChevronVisibility { get; }
    public string ChevronLabel { get; }

    // BreadcrumbBar's overflow flyout shows an item by its text.
    public override string ToString() => Name;
}
