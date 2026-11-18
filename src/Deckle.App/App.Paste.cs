using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Paste.Views;

namespace Deckle.App;

// The Paste to folder window, opened from the tray: a small surface that saves
// what the clipboard holds as a file in a chosen folder. Created on first use
// by the same lazy path as the other windows, placed where the person left it,
// and dropped on close so the next open builds a fresh one.
//
// Always present: it is not a module the person switches off, and it has no
// Settings page (its one setting, the folder, is chosen in the window). Its
// data, the chosen folder, lives in a folder of its own under the data root's
// modules\ like any feature's, so the self-check's data root holds its own.
public partial class App
{
    // The folder name under <UserDataRoot>\modules\. Not a ModuleIds entry:
    // nothing gates on it, it only gives the window a data folder.
    private const string PasteToFolderDataFolder = "paste-to-folder";

    private PasteWindow? _pasteWindow;

    private void ShowPasteWindowLazy()
    {
        EnsurePasteWindow().ShowAndActivate();
        DeckleAppSource.Log.WindowOpened("paste-to-folder");
    }

    // The window created and wired, not shown: the self-check shows it without
    // activating it (SelfCheck/OffscreenWindow.cs).
    private PasteWindow EnsurePasteWindow()
    {
        if (_pasteWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("paste-to-folder",
                () => new PasteWindow(AppPaths.GetModuleDirectory(PasteToFolderDataFolder)));
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.PasteToFolder);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.PasteToFolder);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_pasteWindow, window)) _pasteWindow = null;
            };

            _pasteWindow = window;
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("paste-to-folder", sw.ElapsedMilliseconds);
        }
        return _pasteWindow;
    }
}
