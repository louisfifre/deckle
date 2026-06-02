using Deckle.App.Diagnostics;
using Deckle.Core.Interop;
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
                DeckleAppSource.Log.HotkeyStart(
                    $"{hotkeyName}{(manualProfile is null ? "" : $", LLM: {manualProfile}")}");
                _hudWindow?.ShowPreparing();
                break;

            case ToggleResult.Stopped:
                DeckleAppSource.Log.HotkeyStop();
                break;

            case ToggleResult.IgnoredNoProfile:
                DeckleAppSource.Log.HotkeyNoProfile(hotkeyName);
                break;

            case ToggleResult.IgnoredBusy:
            case ToggleResult.IgnoredDisposed:
                break;
        }
    }
}
