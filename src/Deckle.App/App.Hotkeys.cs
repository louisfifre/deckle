using Deckle.App;
using Deckle.Core;
using Deckle.Transcription;

namespace Deckle.App;

public partial class App
{
    private void OnHotkey(int hotkeyId)
    {
        if (_engine is null) return;

        string hotkeyName = hotkeyId switch
        {
            NativeMethods.HOTKEY_ID_TRANSCRIBE        => "transcribe",
            NativeMethods.HOTKEY_ID_PRIMARY_REWRITE   => "primary rewrite",
            NativeMethods.HOTKEY_ID_SECONDARY_REWRITE => "secondary rewrite",
            _                                         => $"id={hotkeyId}",
        };

        var llm = Llm.Rewrite.LlmSettingsService.Instance.Current;
        string? ResolveSlotName(string? id, string? nameFallback) =>
            (!string.IsNullOrEmpty(id)
                ? llm.Profiles.Find(p => p.Id == id)?.Name
                : null)
            ?? nameFallback;
        string? manualProfile = hotkeyId switch
        {
            NativeMethods.HOTKEY_ID_PRIMARY_REWRITE   =>
                ResolveSlotName(llm.PrimaryRewriteProfileId, llm.PrimaryRewriteProfileName),
            NativeMethods.HOTKEY_ID_SECONDARY_REWRITE =>
                ResolveSlotName(llm.SecondaryRewriteProfileId, llm.SecondaryRewriteProfileName),
            _                                         => null,
        };

        bool isRewriteHotkey = hotkeyId == NativeMethods.HOTKEY_ID_PRIMARY_REWRITE
                            || hotkeyId == NativeMethods.HOTKEY_ID_SECONDARY_REWRITE;

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
