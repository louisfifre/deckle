using Deckle.App;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;

namespace Deckle.App;

public partial class App
{
    private void OnHotkey(int hotkeyId)
    {
        if (_engine is null)
        {
            // Speech isn't provisioned (native runtime + model absent), so the
            // engine was never composed. Rather than do nothing, tell the user
            // at the moment of intent and point them to where they set it up.
            DeckleAppSource.Log.UserFeedbackEmitted(
                0, // Info
                "Dictation isn't set up yet",
                "Open Settings › Dictation to download the speech engine and model.",
                1); // Overlay
            return;
        }

        string hotkeyName = hotkeyId == NativeMethods.HOTKEY_ID_TRANSCRIBE
            ? "transcribe"
            : $"id={hotkeyId}";

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
                    DeckleAppSource.Log.HotkeyStartDetail(hotkeyName);
                }
                _hudWindow?.ShowPreparing();
                break;

            case ToggleResult.Stopped:
                DeckleAppSource.Log.HotkeyStop();
                // Acknowledge the stop on the hotkey thread, the instant the
                // CAS claims it — symmetric with Started → ShowPreparing above.
                // The HUD is otherwise driven by engine status, but the next
                // status ("Transcribing") only fires after the streaming drain
                // has finished decoding the queued tail; until then the chrono
                // would keep ticking and the user would perceive the stop as
                // laggy. Switching here freezes the chrono and shows the
                // finishing affordance immediately; the drain ("the margin")
                // runs invisibly behind it. The later status-driven
                // SwitchToTranscribing is then an idempotent no-op.
                _hudWindow?.SwitchToTranscribing();
                break;

            case ToggleResult.IgnoredBusy:
            case ToggleResult.IgnoredDisposed:
                break;
        }
    }
}
