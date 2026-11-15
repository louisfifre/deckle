using System.Diagnostics;
using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Modules;

namespace Deckle.App;

// The Atelier's window, opened from the tray like the other windows: created
// on first use by the same lazy path, placed where the person left it, and
// dropped on close so the next open builds a fresh one. The module composes
// nothing at start: with no window open it costs nothing. Its data lives in
// the module's own folder of the data root.
public partial class App
{
    private AtelierWindow? _atelierWindow;

    private static bool AtelierPresent => ModulePresence.IsPresent(ModuleIds.Atelier);

    private void ShowAtelierWindowLazy()
    {
        if (!AtelierPresent) return;
        EnsureAtelierWindow().ShowAndActivate();
        DeckleAppSource.Log.WindowOpened("atelier");
    }

    private AtelierWindow EnsureAtelierWindow()
    {
        if (_atelierWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            ConfigureAtelierPhotoDescriptions();
            var window = CreateWindowOrLog("atelier",
                () => new AtelierWindow(AppPaths.GetModuleDirectory(ModuleIds.Atelier)));
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Atelier);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Atelier);
            window.Closed += (_, _) =>
            {
                // A folder being described goes on: it belongs to the app,
                // and the folder shows its progress when it is opened again.
                int runs = AtelierToolCatalog.PhotoDescriptionRunsGoingOn;
                if (runs > 0) Deckle.Atelier.DeckleAtelierSource.Log.PhotoDescriptionRunsOutliveWindow(runs);
                if (ReferenceEquals(_atelierWindow, window)) _atelierWindow = null;
            };

            _atelierWindow = window;
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("atelier", sw.ElapsedMilliseconds);
        }
        return _atelierWindow;
    }

    // The photo tool's descriptions, over Deckle.Llm: built once, cheap
    // (nothing is read until a photo is described). A model set already is
    // kept: the self-check sets a fake one that never reaches a server. The
    // way to the setting exists only with the Rewriting page, which holds it.
    private void ConfigureAtelierPhotoDescriptions()
    {
        if (AtelierToolCatalog.PhotoDescriptionModel is null)
        {
            var model = LlmPhotoDescriptionModel.Create(AppPaths.GetModuleDirectory(ModuleIds.Atelier));
            AtelierToolCatalog.PhotoDescriptionModel = model;
            AtelierToolCatalog.PhotoDescriptionReadiness = model.Readiness;
        }
        if (AtelierToolCatalog.OpenPhotoDescriptionSettings is null && ModulePresence.IsPresent(ModuleIds.Rewrite))
            AtelierToolCatalog.OpenPhotoDescriptionSettings =
                () => ShowSettingsWindowLazy(Llm.Rewrite.Views.LlmSettingsModule.PageTag);
    }

    // The Rewriting page edits the Atelier's choice of model through these
    // two functions (Llm.Rewrite.Views.PhotoDescriptionModelSetting's header):
    // neither library references the other, the host references both.
    private static void ConfigureAtelierPhotoDescriptionSetting()
    {
        if (!AtelierPresent) return;
        var store = new PhotoDescriptionModelChoiceStore(AppPaths.GetModuleDirectory(ModuleIds.Atelier));
        Llm.Rewrite.Views.LlmSettingsModule.PhotoDescriptionModel = new(
            () => store.Read() is { } c ? new Llm.Rewrite.Views.PhotoDescriptionModelPick(c.ProviderId, c.Model) : null,
            pick => store.Write(pick is null ? null : new PhotoDescriptionModelChoice(pick.ProviderId, pick.Model)));
    }
}