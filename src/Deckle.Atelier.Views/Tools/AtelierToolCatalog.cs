using Deckle.Core;

namespace Deckle.Atelier.Views;

// The tools of the window, in the order of their buttons in the top row
// (boards: Fichiers, Photos, Tailles, Carte). One line per tool; the first is
// the one a folder opens in (12.20).
//
// The file tool and the photo tool show and set the same photo marks, so they
// share one queue for the marks' store: a write from one is done before the
// other reads. The store sits in the module's own data folder; the tool seam
// hands no folder to a tool, so the catalog asks AppPaths for the module's,
// by the id the app registers the module under (Deckle.Modules.ModuleIds.Atelier,
// which this library does not reference): the same folder, by the same rule.
internal static class AtelierToolCatalog
{
    private const string ModuleId = "atelier";

    public static IReadOnlyList<IAtelierTool> Create()
    {
        string moduleDirectory = AppPaths.GetModuleDirectory(ModuleId);
        var marks = new PhotoMarkQueue();
        return
        [
            new FilesTool(moduleDirectory, marks),
            new PhotosTool(moduleDirectory, marks),
            new SizesTool(),
            new MapTool(),
        ];
    }
}
