using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Catalog;
using Deckle.Contributions;
using Deckle.Core;
using Deckle.Modules;

namespace Deckle.App;

// The Atelier's window, opened from the tray like the other windows: created
// on first use by the host's lazy path, placed where the person left it, and
// dropped on close so the next open builds a fresh one. Nothing runs at start
// for the window itself: with no window open it costs nothing. Its data lives
// in the module's own folder of the data root.
//
// What it plants at start (Compose): the Rewriting page's choice of the photo
// model, and the Projects window's "Open in Deckle Folders".
internal sealed class AtelierContribution : IContribution
{
    private IContributionContext? _context;

    public string Id => ModuleIds.Atelier;

    public ModuleDescriptor? Descriptor { get; } = new()
    {
        Id = ModuleIds.Atelier,
        Glyph = Glyphs.Folder,
        Order = 700,
    };

    public IReadOnlyList<ContributedWindow> Windows { get; }

    public IReadOnlyList<ContributedWalk> SelfCheckWalks { get; } =
    [
        new ContributedWalk
        {
            WindowName = "atelier",
            ShowsViewsAtRun = true,
            Run = context =>
            {
                SelfCheckWalkContext walk = SelfCheckWalkContext.From(context);
                return walk.App.RunAtelierWalkAsync(walk);
            },
        },
    ];

    public AtelierContribution()
    {
        Windows =
        [
            new ContributedWindow
            {
                Name = "atelier",
                Create = () =>
                {
                    ConfigurePhotoDescriptions();
                    return new AtelierWindow(AppPaths.GetModuleDirectory(ModuleIds.Atelier));
                },
                Show = window => ((AtelierWindow)window).ShowAndActivate(),
                TrayLabelKey = "TrayMenu_Atelier",
                OnClosed = _ =>
                {
                    // A folder being described goes on: it belongs to the app,
                    // and the folder shows its progress when it is opened again.
                    int runs = AtelierToolCatalog.PhotoDescriptionRunsGoingOn;
                    if (runs > 0) DeckleAtelierSource.Log.PhotoDescriptionRunsOutliveWindow(runs);
                },
            },
        ];
    }

    public void Compose(IContributionContext context)
    {
        _context = context;
        if (!context.IsModulePresent(ModuleIds.Atelier)) return;

        // "Open in Deckle Folders" of the Projects window exists only while
        // this module does: a hook left null makes the button absent.
        App.OpenInFoldersHook = OpenInDeckleFoldersAsync;

        // The Rewriting page edits the Atelier's choice of model through these
        // two functions (Llm.Rewrite.Views.PhotoDescriptionModelSetting's
        // header): neither library references the other, the host references
        // both. The page exists only with the Rewriting module.
        if (!context.IsModulePresent(ModuleIds.Rewrite)) return;
        var store = new PhotoDescriptionModelChoiceStore(context.ModuleDirectory(ModuleIds.Atelier));
        Llm.Rewrite.Views.LlmSettingsModule.PhotoDescriptionModel = new(
            () => store.Read() is { } c ? new Llm.Rewrite.Views.PhotoDescriptionModelPick(c.ProviderId, c.Model) : null,
            pick => store.Write(pick is null ? null : new PhotoDescriptionModelChoice(pick.ProviderId, pick.Model)));
    }

    // The photo tool's descriptions, over Deckle.Llm: built once, cheap
    // (nothing is read until a photo is described). A model set already is
    // kept: the self-check sets a fake one that never reaches a server. The
    // way to the setting exists only with the Rewriting page, which holds it.
    private void ConfigurePhotoDescriptions()
    {
        if (AtelierToolCatalog.PhotoDescriptionModel is null)
        {
            var model = LlmPhotoDescriptionModel.Create(AppPaths.GetModuleDirectory(ModuleIds.Atelier));
            AtelierToolCatalog.PhotoDescriptionModel = model;
            AtelierToolCatalog.PhotoDescriptionReadiness = model.Readiness;
        }
        if (AtelierToolCatalog.OpenPhotoDescriptionSettings is null
            && _context is { } context
            && context.IsModulePresent(ModuleIds.Rewrite))
            AtelierToolCatalog.OpenPhotoDescriptionSettings =
                () => context.OpenSettingsPage(Llm.Rewrite.Views.LlmSettingsModule.PageTag);
    }

    // Deckle Folders on a folder it does not hold yet adds it there, then shows
    // it (spec, Open in).
    private static async Task OpenInDeckleFoldersAsync(string path)
    {
        var window = (AtelierWindow)App.PublishedHost.Ensure("atelier");
        window.ShowAndActivate();
        await window.OpenFolderAsync(path);
    }
}
