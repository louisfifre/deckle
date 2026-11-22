using Deckle.Diagnostics;
using Deckle.Modules;
using Deckle.Setup;
using Deckle.Localization;

namespace Deckle.App;

public partial class App
{
    // Set during module composition when transcription is chosen but its speech
    // model is missing or incomplete; null otherwise. Read once, at the end of
    // startup, to offer the repair.
    private string? _speechRepairReason;

    // The engine is composed only at start, so a model that is not there would
    // leave transcription off until the person found the Transcription page. The
    // app instead opens setup on its install step, says why in one line and
    // waits: nothing downloads before the person clicks Download (InstallingPage
    // reads RepairNotice for that).
    private async void OfferSpeechRepair(string reason)
    {
        string workId = WorkId.Next();
        try
        {
            var context = new SetupContext
            {
                SelectedModules = ModulePresence.Choice
                    ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
                RepairNotice = Loc.Get("Setup_Repair_Missing"),
            };

            DeckleAppSource.Log.SpeechRepairOffered(workId, reason, "missing");
            var setup = new SetupWindow(context);
            setup.Body.Navigate(typeof(InstallingPage), setup);
            setup.Activate();

            bool ok = await setup.Completion;
            DeckleAppSource.Log.SpeechRepairEnded(workId, ok ? "completed" : "cancelled");

            // Same landing as the in-app wizard: the engine composes on restart.
            if (ok) RestartApp(Transcription.Views.WhisperSettingsModule.PageTag);
        }
        catch (Exception ex)
        {
            // async void: an escape here would reach the crash handler for a
            // repair the app can run without. The incident stays open, so the
            // tray and the Transcription page still point the person at the fix.
            DeckleAppSource.Log.SpeechRepairFailed(workId, ex.GetType().Name, ex.Message);
        }
    }
}