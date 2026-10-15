using System.Text;

namespace Deckle.Llm.Rewrite;

// Cuts a running dictation into batches that can be rewritten while the
// speaker keeps talking. Pure: no threads, no I/O, one caller at a time
// (DictationBatchRewriter owns the locking).
//
// Utterances join the way the take assembles them in TranscriptionEngine: a
// space, or a paragraph break when the previous utterance ended on a silence.
//
// When a batch closes: once the accumulated text reaches the threshold, at
// the first utterance that ends on a pause. Below the threshold a batch never
// closes, because a short batch gives the profile too little context to
// condense a repeated idea. Waiting for a pause keeps the seam where the
// speaker breathed. The hard cap (twice the threshold) closes a batch without
// a pause, so a speaker who never stops still gets text rewritten during the
// recording instead of one huge batch at Stop.
//
// The carry: a close keeps the unfinished sentence at the end of the text out
// of the batch and starts the next batch with it (SentenceBoundary explains
// why). When the closing utterance ended on a pause, that pause now sits after
// the carried fragment, so the next utterance joins the fragment with a
// paragraph break, as if the fragment were an utterance that ended on silence.
// Flush at Stop has no next batch to carry into, so everything goes.
public sealed class DictationBatcher
{
    readonly int _threshold;
    readonly int _hardCap;
    readonly StringBuilder _open = new();
    bool _pendingParagraphBreak;
    int _nextIndex;

    public DictationBatcher(int thresholdChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdChars);
        _threshold = thresholdChars;
        _hardCap = 2 * thresholdChars;
    }

    // Appends one utterance and returns the batch it closed, or null while the
    // batch stays open. Blank utterances are ignored and leave the pending
    // pause untouched, like a dropped utterance in the take assembly.
    public DictationBatch? Push(string text, bool endedOnSilence)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (_open.Length > 0) _open.Append(_pendingParagraphBreak ? "\n\n" : " ");
        _open.Append(text.Trim());
        _pendingParagraphBreak = endedOnSilence;

        int length = _open.Length;
        if (length < _threshold) return null;
        if (!endedOnSilence && length < _hardCap) return null;

        return Close(endedOnSilence);
    }

    // Closes whatever remains, carry included, or returns null when nothing
    // but whitespace is left.
    public DictationBatch? Flush()
    {
        string text = _open.ToString().Trim();
        bool endsOnPause = _pendingParagraphBreak;
        _open.Clear();
        _pendingParagraphBreak = false;

        if (text.Length == 0) return null;
        return new DictationBatch(_nextIndex++, text, endsOnPause);
    }

    DictationBatch Close(bool endedOnSilence)
    {
        string full = _open.ToString();
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail(full);
        _open.Clear();

        if (carry.Length == 0)
        {
            _pendingParagraphBreak = false;
            return new DictationBatch(_nextIndex++, complete, endedOnSilence);
        }

        // The carried fragment usually belongs to the closing utterance, so the
        // seam falls mid-paragraph. When it came from an utterance of its own
        // after a pause, the paragraph break sat between the batch and the
        // fragment, and the seam keeps it.
        bool breakBeforeCarry = GapContainsParagraphBreak(full, complete.Length);
        _open.Append(carry);
        _pendingParagraphBreak = endedOnSilence;
        return new DictationBatch(_nextIndex++, complete, breakBeforeCarry);
    }

    static bool GapContainsParagraphBreak(string full, int from)
    {
        for (int i = from; i < full.Length && char.IsWhiteSpace(full[i]); i++)
            if (full[i] == '\n') return true;
        return false;
    }
}
