using Deckle.Autocorrect;

namespace Deckle.Autocorrect;

// Chooses among the accent variants of one folded form using left
// context — the preceding words within the sentence, most recent last,
// already lowercased (empty at sentence start). Returns null when no
// candidate clears the margin — the caller then leaves the literal untouched.
//
// trace is the caller's stage ledger: when non-null the disambiguator records its
// per-candidate scores and the margin/evidence gauges into it for the decision
// telemetry. Observation only, null by default.
public interface IPairDisambiguator
{
    string? Choose(
        IReadOnlyList<string> leftContext,
        IReadOnlyList<AccentVariant> candidates,
        StageTrace? trace = null);
}
