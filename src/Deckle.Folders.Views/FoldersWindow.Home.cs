namespace Deckle.Folders.Views;

// The Home page the centre shows when the window stands at Home and a folder
// is added (FoldersWindow.Navigation.cs, ShowHome).
public sealed partial class FoldersWindow
{
    private HomeView? _home;

    private HomeView Home
    {
        get
        {
            if (_home is null)
            {
                _home = new HomeView();
                _home.FolderOpened += OpenHomeTile;
            }
            return _home;
        }
    }

    // A tile opens its folder at its root, as a pane item does; a folder that
    // was forgotten since the tile was drawn is not opened.
    private void OpenHomeTile(AddedFolder folder)
    {
        if (_folders.Find(folder.Path) is null) return;
        _ = NavigateTo(FolderPlace.AtRoot(folder));
    }
}
