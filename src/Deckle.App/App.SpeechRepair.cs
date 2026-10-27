using Deckle.Diagnostics;
using Deckle.Modules;
using Deckle.Setup;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

public partial class App
{
    // Set during module composition when transcription is chosen but its native
    // runtime is missing or does not load here; null otherwise. Read once, at
    // the end of startup, to offer the repair.
    private string? _speechRepairReason;

    // The engine is composed only at start, so a runtime that cannot run would
    // leave transcription off until the person found the Whisper page. The app
    // instead opens setup on its install step, says why in one line and waits:
    // nothing downloads before the person clicks Download (InstallingPage reads
    // RepairNotice for that). A missing model alone keeps the incident only: the
    // Whisper page already offers the model, and nothing about it changed.
    private async void OfferSpeechRepair(string reason)
    {
        string workId = WorkId.Next();
        try
        {
            // An older bundle leaves its own DLLs beside the catalog's: that is
            // the version change the person needs to hear about, not a fault.
            (string notice, string key) =
                NativeRuntime.GetStale().Count > 0          ? ("changed", "Setup_Repair_Changed")
                : reason == SkipNativeRuntimeUnloadable     ? ("does_not_load", "Setup_Repair_DoesNotLoad")
                : ("missing", "Setup_Repair_Missing");

            var context = new SetupContext
            {
                SelectedModel = SpeechModelResolver.ResolveSelection(
                    null, TranscriptionSettingsService.Instance.Current.Engine.Model),
                SelectedModules = ModulePresence.Choice
                    ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
                RepairNotice = Deckle.Catalog.Loc.Get(key),
            };

            DeckleAppSource.Log.SpeechRepairOffered(workId, reason, notice);
            var setup = new SetupWindow(context);
            setup.Body.Navigate(typeof(InstallingPage), setup);
            setup.Activate();

            bool ok = await setup.Completion;
            DeckleAppSource.Log.SpeechRepairEnded(workId, ok ? "completed" : "cancelled");

            // Same landing as the in-app wizard: the engine composes on restart.
            if (ok) RestartApp("Deckle.Transcription.WhisperPage, Deckle.Transcription");
        }
        catch (Exception ex)
        {
            // async void: an escape here would reach the crash handler for a
            // repair the app can run without. The incident stays open, so the
            // tray and the Whisper page still point the person at the fix.
            DeckleAppSource.Log.SpeechRepairFailed(workId, ex.GetType().Name, ex.Message);
        }
    }
}
