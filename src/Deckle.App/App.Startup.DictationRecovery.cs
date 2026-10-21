using Deckle.Core;
using Deckle.Transcription;

namespace Deckle.App;

// A file in recovery/ at launch means the previous process died during a
// dictation (the engine deletes it as soon as a take is delivered or
// discarded). The sealed raw text goes back on the clipboard and the HUD says
// so once, with no prompt and no dialog.
//
// Independent of the engine on purpose: the transcription module may be absent
// or unprovisioned, and the file would still deserve its restore. The text is
// personal data, so nothing below logs it, only the character count.
public partial class App
{
    private void RestoreInterruptedDictation()
    {
        var store = new DictationRecoveryStore(AppPaths.DictationRecoveryFilePath);
        DictationRecoveryReadStatus status = store.TryRead(out string text);
        if (status == DictationRecoveryReadStatus.Missing) return;

        DeckleAppSource.Log.DictationRecoveryFound();

        // Unreadable (another Windows account, corruption): nothing can be
        // done with it, and keeping it would repeat the warning at every launch.
        if (status == DictationRecoveryReadStatus.Unreadable || string.IsNullOrWhiteSpace(text))
        {
            string reason = status == DictationRecoveryReadStatus.Unreadable ? "unreadable" : "empty";
            bool deleted = store.Delete();
            DeckleAppSource.Log.DictationRecoveryUnreadable(reason, deleted);
            return;
        }

        // The file is the only copy: it goes only once the clipboard has the text.
        ClipboardWriteResult write = Win32ClipboardWriter.Instance.TryWriteText(text);
        if (!write.Landed)
        {
            DeckleAppSource.Log.DictationRecoveryClipboardFailed(ClipboardFailureReason(write.Status), text.Length);
            return;
        }

        _hudWindow?.ShowDictationRecovered();
        DeckleAppSource.Log.DictationRecovered();
        DeckleAppSource.Log.DictationRecoveredDetail(text.Length);
        if (!store.Delete())
            DeckleAppSource.Log.DictationRecoveryKept("delete_failed");
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
