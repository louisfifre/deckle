using Deckle.Folders;
using Deckle.Core;

namespace Deckle.Folders.Views;

// The one way another window of Deckle asks this one to show a folder. It sits
// in its own file so the window's other partials stay as they are: Deckle Folders
// does not know who asks. Deckle Projects' "Open in Deckle Folders" button was
// wired to it while both windows lived in one process; they are two apps now,
// so that button is absent until the apps can reach each other.
public sealed partial class FoldersWindow
{
    // Shows a folder in the file tool. A folder this window does not hold yet is
    // added first, a folder it holds is opened (AddAndOpen does both); a pane
    // over the page is closed so the folder is seen, as after the picker.
    public Task OpenFolderAsync(string path)
    {
        _pane.CloseIfOverPage();
        return AddAndOpen(FolderPaths.Normalize(path));
    }
}
