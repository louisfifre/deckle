using Deckle.Autocorrect;

namespace Deckle.Autocorrect;

// Lets a caller ask commit policies which words are genuinely ambiguous slots:
// accent variants sharing one folded form (a/à, cote/côté), or bounded keyboard
// neighbours the instant typo stage declined because no candidate dominated.
// A bare null from Evaluate does not say this: it conflates "left literal",
// "blacklisted", and "ambiguous, deferred". The reranker needs an explicit
// closed candidate set, and only for that real residue.
public interface IAmbiguityProbe
{
    // The closed candidate set for a commit the instant gate left literal;
    // empty when there is nothing safe to disambiguate.
    IReadOnlyList<AccentVariant> AmbiguousCandidates(string word);

    // The full candidate set used by offline sentence studies. Multiple commit
    // policies may contribute because the study starts from typed text without a
    // live decision that says which stage acted.
    IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral);

    // The closed set allowed to take back a correction the commit stage already
    // applied. A single policy defaults to its sentence candidates. A composite
    // narrows this to the policy that owned the correction, rather than granting
    // unrelated policies a second search over the typed word.
    IReadOnlyList<AccentVariant> CorrectionCandidates(string typedWord) =>
        SentenceCandidates(typedWord, includeTypedLiteral: true);
}
