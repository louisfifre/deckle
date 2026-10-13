namespace Deckle.Autocorrect.Lab;

// On-disk shape of the observation harvest, sealed at rest with DPAPI by
// HarvestStore. Two filtered signal streams, nothing else: the backspace-retype
// correction pairs, and the committed words the French lexicon does not know.
// The raw typed stream is never persisted. Aggregation is pure in-memory
// mutation, time passed in, so it is testable without disk or DPAPI.
public sealed class HarvestData
{
    public List<HarvestedEdit> Edits { get; set; } = new();
    public List<HarvestedWord> UnknownWords { get; set; } = new();

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

// A correction the user made by hand: typed Original, went back, retyped
// Replacement.
public sealed class HarvestedEdit
{
    public string Original { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

public sealed class HarvestedWord
{
    public string Word { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
