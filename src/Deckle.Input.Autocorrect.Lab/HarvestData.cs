namespace Deckle.Input.Autocorrect.Lab;

// On-disk shape of the observation harvest — the maintainer's iteration corpus,
// captured by the `harvest` command and (unlike the personal dictionary) sealed
// at rest with DPAPI. Two filtered signal streams, nothing else: the
// backspace-retape correction pairs, and the committed words the French lexicon
// does not know. The raw typed stream is never persisted.
//
// Aggregation lives here as pure in-memory mutation — no I/O, time passed in —
// so the dedup / count behaviour is testable without touching disk or DPAPI;
// HarvestStore wraps this with encrypted persistence.
public sealed class HarvestData
{
    public List<HarvestedEdit> Edits { get; set; } = new();
    public List<HarvestedWord> UnknownWords { get; set; } = new();

    // Records a (typed → retyped) correction pair, aggregated by the exact
    // surface pair: a repeat bumps the count and the last-seen stamp.
    public void RecordEdit(string original, string replacement, DateTimeOffset now)
    {
        var entry = Edits.FirstOrDefault(e =>
            string.Equals(e.Original, original, StringComparison.Ordinal) &&
            string.Equals(e.Replacement, replacement, StringComparison.Ordinal));

        if (entry is null)
        {
            Edits.Add(new HarvestedEdit
            {
                Original = original,
                Replacement = replacement,
                Count = 1,
                FirstSeenUtc = now,
                LastSeenUtc = now,
            });
            return;
        }

        entry.Count++;
        entry.LastSeenUtc = now;
    }

    // Records a committed word absent from the lexicon, aggregated by the exact
    // surface form.
    public void RecordUnknownWord(string word, DateTimeOffset now)
    {
        var entry = UnknownWords.FirstOrDefault(w =>
            string.Equals(w.Word, word, StringComparison.Ordinal));

        if (entry is null)
        {
            UnknownWords.Add(new HarvestedWord
            {
                Word = word,
                Count = 1,
                FirstSeenUtc = now,
                LastSeenUtc = now,
            });
            return;
        }

        entry.Count++;
        entry.LastSeenUtc = now;
    }
}

// A backspace-retape correction the user made by hand: « typed Original, went
// back, retyped Replacement » — the raw material of a conservative typo channel.
public sealed class HarvestedEdit
{
    public string Original { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

// A committed word the French lexicon does not carry — the coverage gap
// (conjugations absent from Lexique, personal vocabulary, proper nouns).
public sealed class HarvestedWord
{
    public string Word { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
