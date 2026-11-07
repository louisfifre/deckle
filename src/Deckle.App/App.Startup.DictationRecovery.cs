using Deckle.Catalog;
using Deckle.Core;
using Deckle.Transcription;

namespace Deckle.App;

// A file in recovery/ at launch means the previous process died during a
// dictation (the engine deletes it as soon as a take is delivered or
// discarded). The sealed raw text goes back on the clipboard and the HUD says
// so once, with no prompt and no dialog.
//
// Two files can be there. The audio of the take (take-audio.dat) is the most
// complete: when the engine exists it is transcribed once and its text goes to
// the clipboard (TakeAudioRecovery holds the decision and the cleanup order).
// The text copy (dictation.dat) is the fallback, restored whenever the audio
// did not deliver, so a take is never left with nothing: the clipboard is
// written once, by whichever recovery delivers.
//
// The text restore is independent of the engine on purpose: the transcription
// module may be absent or unprovisioned, and the file would still deserve its
// restore. The text is personal data, so nothing below logs it, only the
// character count.
public partial class App
{
    private void RecoverInterruptedDictation()
    {
        TranscriptionEngine? engine = _engine;
        var audio = new TakeAudioRecoveryStore(AppPaths.TakeAudioRecoveryFilePath);
        if (engine is null || !(audio.Exists || audio.AttemptedExists))
        {
            RestoreInterruptedDictation();
            return;
        }

        var recovery = new TakeAudioRecovery(
            audio,
            new DictationRecoveryStore(AppPaths.DictationRecoveryFilePath),
            engine.RecoverTakeFromAudioAsync);

        // TryRecoverAsync runs on this thread up to the engine call: it sets the
        // file aside, reads it and puts the engine to work. That is on purpose.
        // The UI thread does not pump hotkeys until startup returns, so no take
        // can start and replace the file in between, and the engine is busy
        // from the moment the hotkeys can fire. The model load and the
        // transcription then run on the engine's worker, and the HUD shows
        // its "transcribing" state meanwhile.
        _ = FinishDictationRecoveryAsync(recovery.TryRecoverAsync());
    }

    private async Task FinishDictationRecoveryAsync(Task<TakeAudioRecoveryResult> pending)
    {
        try
        {
            // Continues off the UI thread; the HUD, the clipboard and the files
            // are safe from there, as the engine's own worker uses them too.
            TakeAudioRecoveryResult result = await pending.ConfigureAwait(false);
            if (result.Failed)
            {
                // The failure leads. The text copy is still restored (it may be
                // all that is left), but without its "recovered" success, which
                // would tell the person the whole take is back.
                bool textRestored = RestoreInterruptedDictation(announce: false);
                _hudWindow?.ShowError(
                    Loc.Get("Hud_DictationRecoveryFailed_Title"),
                    Loc.Get(textRestored
                        ? "Hud_DictationRecoveryFailed_TextOnly_Body"
                        : "Hud_DictationRecoveryFailed_Body"));
                return;
            }

            if (!result.Recovered)
            {
                RestoreInterruptedDictation();
                return;
            }

            _hudWindow?.ShowDictationRecovered();
            DeckleAppSource.Log.DictationRecovered();
            DeckleAppSource.Log.DictationRecoveredDetail(result.Chars);
        }
        catch (Exception ex)
        {
            // Neither the recovery nor the text restore is meant to throw, but
            // nothing may escape a fire-and-forget task. A failure of the audio
            // flow must not cost the text copy: it is restored here, and a no-op
            // when the flow got as far as deleting it.
            DeckleAppSource.Log.CleanupFailed("dictation_recovery", ex.GetType().Name, ex.Message);
            try
            {
                RestoreInterruptedDictation();
            }
            catch (Exception restoreEx)
            {
                DeckleAppSource.Log.CleanupFailed(
                    "dictation_text_recovery", restoreEx.GetType().Name, restoreEx.Message);
            }
        }
    }

    // Returns true when the text reached the clipboard. announce=false keeps the
    // HUD silent for a caller that tells the outcome itself.
    private bool RestoreInterruptedDictation(bool announce = true)
    {
        var store = new DictationRecoveryStore(AppPaths.DictationRecoveryFilePath);
        DictationRecoveryReadStatus status = store.TryRead(out string text);
        if (status == DictationRecoveryReadStatus.Missing) return false;

        DeckleAppSource.Log.DictationRecoveryFound();

        // Unreadable (another Windows account, corruption): nothing can be
        // done with it, and keeping it would repeat the warning at every launch.
        if (status == DictationRecoveryReadStatus.Unreadable || string.IsNullOrWhiteSpace(text))
        {
            string reason = status == DictationRecoveryReadStatus.Unreadable ? "unreadable" : "empty";
            bool deleted = store.Delete();
            DeckleAppSource.Log.DictationRecoveryUnreadable(reason, deleted);
            return false;
        }

        // The file is the only copy: it goes only once the clipboard has the text.
        ClipboardWriteResult write = Win32ClipboardWriter.Instance.TryWriteText(text);
        if (!write.Landed)
        {
            DeckleAppSource.Log.DictationRecoveryClipboardFailed(ClipboardFailureReason(write.Status), text.Length);
            return false;
        }

        if (announce) _hudWindow?.ShowDictationRecovered();
        DeckleAppSource.Log.DictationRecovered();
        DeckleAppSource.Log.DictationRecoveredDetail(text.Length);
        if (!store.Delete())
            DeckleAppSource.Log.DictationRecoveryKept("delete_failed");
        return true;
    }

    // Shared with LogWindow's copy, so a clipboard status reads the same
    // whichever surface reports it.
    internal static string ClipboardFailureReason(ClipboardWriteStatus status) => status switch
    {
        ClipboardWriteStatus.AllocFailed          => "alloc_failed",
        ClipboardWriteStatus.OpenFailed           => "clipboard_busy",
        ClipboardWriteStatus.SetDataFailed        => "set_data_refused",
        ClipboardWriteStatus.VerifyMissing        => "verify_missing",
        ClipboardWriteStatus.VerifyLengthMismatch => "verify_length_mismatch",
        _                                         => "unknown",
    };
}
