namespace Deckle.Input.Autocorrect.Lexicon;

// One accented surface form behind a folded key, with its corpus
// frequency (occurrences per million words, the Lexique scale).
public readonly record struct AccentVariant(string Form, double FrequencyPerMillion);
