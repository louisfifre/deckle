namespace Deckle.Autocorrect;

// The exact visible sentence and every bounded one-edit alternative judged at
// sentence close. Positions refer to Literal, so punctuation and separators are
// never reconstructed from word tokens before scoring.
public sealed record ClosedSentenceTransaction(
    string Literal,
    IReadOnlyList<string> Words,
    IReadOnlyList<SentenceEditCandidate> Edits);

// One positional replacement in the exact literal. SlotIndex is retained only
// to map a winning edit back to the coordinator's tracked word.
public readonly record struct SentenceEditCandidate(
    int SlotIndex,
    int Start,
    int Length,
    string Replacement);
