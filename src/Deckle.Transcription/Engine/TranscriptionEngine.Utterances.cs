namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // Raised from the streaming consumer task for every utterance that
    // contributes text, in emission order, after that text has been appended
    // to the take. Live dictation and file transcription share the consumer,
    // so both raise it. Fires on a background thread — subscribers marshal.
    //
    // For the feeds that need the utterance as a unit (the batch rewriter, the
    // crash-safety recovery file). A surface that shows the text subscribes to
    // TranscriptChanged instead, which carries the text as it will be
    // delivered, rewrites included.
    public event Action<TranscribedUtterance>? UtteranceTranscribed;

    // Raised whenever the delivered text of the current take changes: after
    // each utterance joins it (Appended), after a rewrite batch returns during
    // the recording and when the whole-take rewrite replaces the raw text at
    // finalize (Revised). The snapshot is the whole text, so a reader shows
    // it as is and never assembles anything. Fires on background threads
    // (the consumer task, a rewrite's thread-pool thread, the worker) —
    // subscribers marshal. Raised after UtteranceTranscribed for the same
    // utterance, once the batch rewriter has taken it.
    public event Action<TranscriptSnapshot>? TranscriptChanged;

    private void RaiseTranscriptChanged(string text, TranscriptChangeKind kind) =>
        TranscriptChanged?.Invoke(new TranscriptSnapshot(text, kind));
}
