using Deckle.Input.Autocorrect.Lexicon;

namespace Deckle.Input.Autocorrect.Engine;

// Chooses among the accent variants of one folded form using left
// context. Returns null when no candidate clears the margin — the
// caller then leaves the literal untouched.
public interface IPairDisambiguator
{
    string? Choose(string? previousWord, IReadOnlyList<AccentVariant> candidates);
}
