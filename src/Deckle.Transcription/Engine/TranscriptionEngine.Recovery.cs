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
    //      is written by the first utterance that carries text, so a take that
    //      never gets a word leaves nothing behind.
    //   2. FinalizeTranscription deletes it the moment the raw copy lands on
    //      the clipboard, before the rewrite and the paste: from there the
    //      clipboard holds the text, and a later failure cannot leave a file
    //      that would overwrite the clipboard at the next launch.
    //   3. Every other ending keeps it for the next launch to restore: the raw
    //      copy failed, the microphone dropped after words were transcribed,
    //      the app quit or restarted mid-take, or the pipeline crashed. Each of
    //      these leaves the file as the only copy of what was said.
    //
    // Known limit: while the app keeps running, the next dictation's first
    // utterance overwrites a kept file. Only one take is ever recoverable.
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
            // Blank text would only replace a kept file from an earlier take
            // with nothing worth restoring.
            string text = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(text)) store.TryWrite(text);
        };
        UtteranceTranscribed += handler;
        return new UtteranceSubscription(this, handler);
    }

    // The clipboard holds the take's text. Never throws: the store logs its
    // own failure.
    private void ClearRecovery() => _recovery?.Delete();
}
