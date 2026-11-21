using Deckle.Folders.Views;
using Deckle.Contributions;
using Deckle.Core;
using Deckle.Modules;

namespace Deckle.Folders.App;

// The Deckle Folders window, the only window of its app: created on
// first use by the host's lazy path, placed where the person left it. Its data
// lives in the module's folder of this app's own data root
// (apps\folders\modules\folders). It left the published Deckle: that Deckle
// carries nothing of it, and the Projects window's "Open in Deckle Folders" is
// absent there.
//
// Photo descriptions: the window's photo tool describes through Deckle.Llm
// (LlmPhotoDescriptionModel), wired when the window is created. This app has no
// Settings window, so nothing edits the choice of model yet: the tool says so
// in its readiness text (a later decision, with the app's own settings).
internal sealed class FoldersContribution : IContribution
{
    // The module's data folder under the app's root, and the contribution's id.
    internal const string ModuleId = "folders";

    public string Id => ModuleId;

    // Not a module of a catalogue: this app has no module chooser.
    public ModuleDescriptor? Descriptor => null;

    public IReadOnlyList<ContributedWindow> Windows { get; } =
    [
        new ContributedWindow
        {
            Name = "folders",
            Create = () =>
            {
                ConfigurePhotoDescriptions();
                return new FoldersWindow(AppPaths.GetModuleDirectory(ModuleId));
            },
            Show = window => ((FoldersWindow)window).ShowAndActivate(),
            OnClosed = _ =>
            {
                // A folder being described goes on: it belongs to the app,
                // and the folder shows its progress when it is opened again.
                int runs = FoldersToolCatalog.PhotoDescriptionRunsGoingOn;
                if (runs > 0) DeckleFoldersSource.Log.PhotoDescriptionRunsOutliveWindow(runs);
            },
        },
    ];

    public IReadOnlyList<ContributedWalk> SelfCheckWalks { get; } =
    [
        new ContributedWalk
        {
            WindowName = "folders",
            ShowsViewsAtRun = true,
            Run = context => FoldersSelfCheck.RunFoldersWalkAsync(FoldersWalkContext.From(context)),
        },
    ];

    public void Compose(IContributionContext context)
    {
        // Nothing to plant: the window reaches nothing outside itself.
    }

    // The photo tool's descriptions, over Deckle.Llm: built once, cheap
    // (nothing is read until a photo is described). A model set already is
    // kept: the self-check sets a fake one that never reaches a server.
    private static void ConfigurePhotoDescriptions()
    {
        if (FoldersToolCatalog.PhotoDescriptionModel is not null) return;
        var model = LlmPhotoDescriptionModel.Create(AppPaths.GetModuleDirectory(ModuleId));
        FoldersToolCatalog.PhotoDescriptionModel = model;
        FoldersToolCatalog.PhotoDescriptionReadiness = model.Readiness;
    }
}
