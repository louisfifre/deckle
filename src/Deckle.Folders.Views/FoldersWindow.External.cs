using Deckle.Folders;
using Deckle.Core;

namespace Deckle.Folders.Views;

// The one way another window of Deckle asks this one to show a folder. It sits
// in its own file so the window's other partials stay as they are: Deckle Folders
// does not know who asks (the host wires Deckle Projects' "Open in Deckle
// Folders" button to it, App.Projects.cs).
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
