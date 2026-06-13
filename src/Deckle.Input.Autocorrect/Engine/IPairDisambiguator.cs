using Deckle.Input.Autocorrect.Lexicon;

namespace Deckle.Input.Autocorrect.Engine;

// Chooses among the accent variants of one folded form using left
// context — the preceding words within the sentence, most recent last,
// already lowercased (empty at sentence start). Returns null when no
// candidate clears the margin — the caller then leaves the literal untouched.
public interface IPairDisambiguator
{
    string? Choose(IReadOnlyList<string> leftContext, IReadOnlyList<AccentVariant> candidates);
}
