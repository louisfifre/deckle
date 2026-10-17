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
            DeckleAppSource.Log.DictationRecoveryUnreadable();
            store.Delete();
            return;
        }

        // The file is the only copy: it goes only once the clipboard has the text.
        if (!Win32ClipboardWriter.Instance.TryWriteText(text).Landed)
        {
            DeckleAppSource.Log.DictationRecoveryClipboardFailed();
            return;
        }

        _hudWindow?.ShowDictationRecovered();
        DeckleAppSource.Log.DictationRecovered();
        DeckleAppSource.Log.DictationRecoveredDetail(text.Length);
        store.Delete();
    }
}
