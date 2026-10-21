using Deckle.App;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;

namespace Deckle.App;

public partial class App
{
    // The tray panel's Record button lands here too.
    private void OnHotkey(int hotkeyId)
    {
        _hotkeyActions.FirstOrDefault(action => action.Id == hotkeyId)?.Invoke();
    }

    private void ToggleRecordingFromHotkey()
    {
        if (_engine is null)
        {
            // No engine means speech is not provisioned.
            DeckleAppSource.Log.HotkeyPressIgnored("engine_not_composed");
            DeckleAppSource.Log.UserFeedbackEmitted(
                0, // Info
                "Dictation isn't set up yet",
                "Open Settings › Dictation to download the speech engine and model.",
                1); // Overlay
            return;
        }

        // The press carries no profile: the engine applies the persisted
        // active rewrite profile at finalize.
        var result = _engine.RequestToggle(
            shouldPaste: Settings.SettingsService.Instance.Current.Paste.AutoPasteEnabled);

        switch (result)
        {
            case ToggleResult.Started:
                DeckleAppSource.Log.HotkeyStart();
                if (OperationalLogAdmission.IsDetailEnabled(
                        OperationalLogActivity.Transcription,
                        DeckleAppSource.Log,
                        System.Diagnostics.Tracing.EventLevel.Verbose,
                        (System.Diagnostics.Tracing.EventKeywords)Keywords.Capture))
                {
                    // The tray's Record item runs this same path: the label
                    // is the bound chord, not proof it was pressed.
                    DeckleAppSource.Log.HotkeyStartDetail(HotkeyChordLabel(RecordHotkeyId));
                }
                _hudWindow?.ShowPreparing();
                break;

            case ToggleResult.Stopped:
                DeckleAppSource.Log.HotkeyStop();
                // Acknowledged here at once: the next engine status only fires
                // after the streaming drain decodes the queued tail, so the chrono
                // would keep ticking and the stop would feel laggy. The later
                // status-driven SwitchToTranscribing is an idempotent no-op.
                _hudWindow?.SwitchToTranscribing();
                break;

            case ToggleResult.IgnoredBusy:
            case ToggleResult.IgnoredDisposed:
                break;
        }
    }
}
