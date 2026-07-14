using Deckle.App;
using Deckle.Core;
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

        string hotkeyName = hotkeyId switch
        {
            NativeMethods.HOTKEY_ID_TRANSCRIBE        => "transcribe",
            NativeMethods.HOTKEY_ID_PRIMARY_REWRITE   => "primary rewrite",
            NativeMethods.HOTKEY_ID_SECONDARY_REWRITE => "secondary rewrite",
            _                                         => $"id={hotkeyId}",
        };

        bool isRewriteHotkey = hotkeyId == NativeMethods.HOTKEY_ID_PRIMARY_REWRITE
                            || hotkeyId == NativeMethods.HOTKEY_ID_SECONDARY_REWRITE;

        // Profile slots are read only when a rewrite chord fired. Guarding
        // the read keeps the plain transcribe path from ever touching the
        // Rewrite module's settings service — with the module absent its
        // chords are not even registered (see hotkeyIds in OnLaunched), so
        // this branch cannot run and the service is never instantiated.
        string? manualProfile = null;
        if (isRewriteHotkey)
        {
            var llm = Llm.Rewrite.LlmSettingsService.Instance.Current;
            string? ResolveSlotName(string? id, string? nameFallback) =>
                (!string.IsNullOrEmpty(id)
                    ? llm.Profiles.Find(p => p.Id == id)?.Name
                    : null)
                ?? nameFallback;
            manualProfile = hotkeyId == NativeMethods.HOTKEY_ID_PRIMARY_REWRITE
                ? ResolveSlotName(llm.PrimaryRewriteProfileId, llm.PrimaryRewriteProfileName)
                : ResolveSlotName(llm.SecondaryRewriteProfileId, llm.SecondaryRewriteProfileName);
        }

        var result = _engine.RequestToggle(
            manualProfileName: manualProfile,
            shouldPaste: Settings.SettingsService.Instance.Current.Paste.AutoPasteEnabled,
            requireProfile: isRewriteHotkey);

        switch (result)
        {
            case ToggleResult.Started:
                DeckleAppSource.Log.HotkeyStart();
                DeckleAppSource.Log.HotkeyStartDetail(
                    $"{hotkeyName}{(manualProfile is null ? "" : $", LLM: {manualProfile}")}");
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

            case ToggleResult.IgnoredNoProfile:
                DeckleAppSource.Log.HotkeyNoProfile();
                DeckleAppSource.Log.HotkeyNoProfileDetail(hotkeyName);
                break;

            case ToggleResult.IgnoredBusy:
            case ToggleResult.IgnoredDisposed:
                break;
        }
    }
}
