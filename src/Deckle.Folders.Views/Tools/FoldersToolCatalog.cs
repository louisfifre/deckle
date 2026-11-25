using Deckle.Core;

namespace Deckle.Folders.Views;

// The tools of the window, in the order of their buttons in the top row
// (boards: Photos, Tailles, Carte; Files, the first, has no button: it shows
// when none is pressed). One line per tool; the first is the one a folder
// opens in (12.20).
//
// The file tool and the photo tool show and set the same photo marks, so they
// share one queue for the marks' store: a write from one is done before the
// other reads. The store sits in the module's own data folder; the tool seam
// hands no folder to a tool, so the catalog asks AppPaths for the module's,
// by the id the app registers the module under (Deckle.Modules.ModuleIds.Folders,
// which this library does not reference): the same folder, by the same rule.
//
// The model that describes photos comes from the host: this library knows no
// model and no server (IPhotoDescriptionModel's header), so the app sets
// PhotoDescriptionModel before it opens the window, with what tells, without
// asking a server, whether the model can be used (PhotoDescriptionReadiness)
// and the way to its setting (OpenPhotoDescriptionSettings). Null outside the
// app (a test, a window built alone): the tools then have no description to
// offer.
//
// The two tools share the descriptions too (PhotoDescriptions), and those
// belong to the process rather than to a window: one is built on the first
// window and kept, so a folder being described goes on when its window
// closes, and shows its progress again in the next one.
public static class FoldersToolCatalog
{
    private const string ModuleId = "folders";

    private static readonly object DescriptionsLock = new();
    private static PhotoDescriptions? _descriptions;

    public static IPhotoDescriptionModel? PhotoDescriptionModel { get; set; }

    // Null, or the failure every description would meet as the model is set
    // up (no model chosen, a provider not on this machine). Read off the UI
    // thread. Unset, the model is taken as usable.
    public static Func<PhotoDescriptionFailure?>? PhotoDescriptionReadiness { get; set; }

    // Opens Deckle's settings where the model is chosen; unset when the app
    // has no such page.
    public static Action? OpenPhotoDescriptionSettings { get; set; }

    // The folders being described right now, for the host to say so when the
    // window closes.
    public static int PhotoDescriptionRunsGoingOn => _descriptions?.RunsGoingOn ?? 0;

    internal static IReadOnlyList<IFoldersTool> Create()
    {
        string moduleDirectory = AppPaths.GetModuleDirectory(ModuleId);
        var marks = new PhotoMarkQueue();
        PhotoDescriptions? descriptions = Descriptions(moduleDirectory);
        return
        [
            new FilesTool(moduleDirectory, marks, descriptions),
            new PhotosTool(moduleDirectory, marks, descriptions),
            new SizesTool(),
            new MapTool(),
        ];
    }

    private static PhotoDescriptions? Descriptions(string moduleDirectory)
    {
        if (PhotoDescriptionModel is not { } model) return null;
        lock (DescriptionsLock)
        {
            Func<PhotoDescriptionFailure?> readiness = () => PhotoDescriptionReadiness?.Invoke();
            return _descriptions ??= new PhotoDescriptions(
                new PhotoDescriptionQueue(new PhotoDescriber(model)), moduleDirectory, readiness,
                () => OpenPhotoDescriptionSettings);
        }
    }
}
