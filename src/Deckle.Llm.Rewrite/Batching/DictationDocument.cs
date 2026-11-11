using System.Linq;
using System.Text;

namespace Deckle.Llm.Rewrite;

// The document of one take: its passages in speech order, each with its raw
// text and the reading it shows, then the text still open after them. The
// transcript panel and the clipboard read it; DictationBatchRewriter writes
// it as rewrites come back.
//
// Four guarantees, the reason it exists apart from the rewriter:
//   - the raw text of a passage never changes, and every rewrite starts from
//     it, never from an earlier rewrite (two voices would mix otherwise);
//   - a reading older than the one shown is ignored: each rewrite is tagged
//     with the generation it was sent in (a generation per streaming
//     profile, DictationBatchRewriter.Use), and a passage only moves forward;
//   - a failed rewrite leaves the reading the passage had;
//   - reading the document never blocks: one lock, held only to copy.
//
// Generation 0 is the raw text: a passage nobody rewrote yet shows it.
public sealed class DictationDocument
{
    readonly object _gate = new();
    readonly List<Passage> _passages = new();
    string _open = "";

    public int PassageCount
    {
        get { lock (_gate) return _passages.Count; }
    }

    // Appends a closed passage; its Index must be the next one.
    internal void Add(DictationBatch batch)
    {
        lock (_gate)
        {
            if (batch.Index != _passages.Count)
                throw new ArgumentException(
                    $"Passage {batch.Index} added after {_passages.Count} passages.", nameof(batch));
            _passages.Add(new Passage(batch));
        }
    }

    // The text after the last closed passage, as said.
    internal void SetOpen(string text)
    {
        lock (_gate) _open = text;
    }

    // The closed passages' raw text, in order.
    internal DictationBatch[] Batches()
    {
        lock (_gate) return _passages.Select(passage => passage.Batch).ToArray();
    }

    // Records a rewrite of passage index sent in generation. Returns false
    // when it changes nothing: a failure (null text) or a reading older than
    // or as old as the one shown.
    internal bool Apply(int index, int generation, string? text)
    {
        if (text is null) return false;
        lock (_gate)
        {
            Passage passage = _passages[index];
            if (generation <= passage.Generation) return false;
            passage.Reading = text;
            passage.Generation = generation;
            return true;
        }
    }

    // The generation each passage shows, 0 for raw.
    internal int[] Generations()
    {
        lock (_gate) return _passages.Select(passage => passage.Generation).ToArray();
    }

    // The take as it reads right now: each passage's reading in order (its raw
    // text until a rewrite came back), then the open text. A paragraph break
    // after a passage that ended on a pause, a space otherwise, the way the
    // take's raw text is joined.
    public string Snapshot()
    {
        (DictationBatch Batch, string? Reading)[] passages;
        string open;
        lock (_gate)
        {
            passages = _passages.Select(passage => (passage.Batch, passage.Reading)).ToArray();
            open = _open;
        }

        var text = new StringBuilder();
        DictationBatch? previous = null;
        foreach ((DictationBatch batch, string? reading) in passages)
        {
            if (previous is not null) text.Append(previous.EndsOnPause ? "\n\n" : " ");
            text.Append(reading ?? batch.Text);
            previous = batch;
        }

        if (open.Length > 0)
        {
            if (previous is not null) text.Append(previous.EndsOnPause ? "\n\n" : " ");
            text.Append(open);
        }

        return text.ToString();
    }

    // Guarded by the document's lock.
    sealed class Passage(DictationBatch batch)
    {
        public DictationBatch Batch { get; } = batch;
        public string? Reading;
        public int Generation;
    }
}
