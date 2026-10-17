using System.Text;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Recovery partial — the crash-safety copy of a live dictation ───────────
    //
    // A dictation lives in memory until Stop, so a crash mid-take loses
    // everything already said. This partial keeps a sealed copy of the raw text
    // on disk (DictationRecoveryStore) and rewrites it at every utterance, with
    // no debounce: an utterance is seconds apart, and the write is a few
    // kilobytes at most.
    //
    // Lifecycle, in the order it happens:
    //   1. ProduceStreamingAsync subscribes FeedRecovery for the take. The file
    //      is written by the first utterance, so a take that never gets a word
    //      leaves nothing behind.
    //   2. WorkerRun deletes it once FinalizeTranscription has delivered the
    //      text (pasted or clipboard only), and also when the take produced
    //      nothing: cancelled, aborted, or empty.
    //   3. When the raw copy to the clipboard itself failed (outcome None), the
    //      file is the only copy left, so it stays for the next launch to
    //      restore. A crash of the pipeline keeps it for the same reason.
    //
    // Scope: live dictation only. UtteranceTranscribed also fires for a file
    // transcription, which has its own saved .txt and must not touch the
    // dictation recovery file. The monolithic strategy has no utterance event,
    // so it never writes. A null store switches the whole mechanism off, which
    // is what every test engine built with the default constructors gets.
    //
    // Threads: the feed runs on the consumer task, before finalize, and the
    // delete runs on the worker thread after the consumer drain has ended, so a
    // write never races a delete. The store serializes its own file access.

    // Null when no store was injected. The caller disposes the handle on every
    // exit, so a later take or a file run never feeds this take's buffer.
    private IDisposable? FeedRecovery()
    {
        DictationRecoveryStore? store = _recovery;
        if (store is null) return null;

        // Mirrors the join rule of the consumer loop: a space, or a paragraph
        // break after a silence cut. It is rebuilt here, not read from the
        // loop, so the file always holds the raw text the clipboard receives.
        var buffer = new StringBuilder();
        bool pendingParagraphBreak = false;
        Action<TranscribedUtterance> handler = u =>
        {
            if (buffer.Length > 0) buffer.Append(pendingParagraphBreak ? "\n\n" : " ");
            buffer.Append(u.Text);
            pendingParagraphBreak = u.EndedOnSilence;
            store.TryWrite(buffer.ToString());
        };
        UtteranceTranscribed += handler;
        return new UtteranceSubscription(this, handler);
    }

    // The take is over and its text reached its destination, or there was
    // nothing to keep. Never throws: the store logs its own failure.
    private void ClearRecovery() => _recovery?.Delete();
}
