namespace Deckle.Autocorrect;

// What a training pass observed — a data-quality signal, not engine state.
// It lives in the engine because PairModel carries it at runtime too: the
// offline trainer fills the counters, while a model loaded from disk reports
// only its row count. The trainer that produces it is in Deckle.Autocorrect.Lab.
public sealed record TrainerReport(
    long Sentences,
    long Tokens,
    long AmbiguousSlotOccurrences,
    long KeptRows);
