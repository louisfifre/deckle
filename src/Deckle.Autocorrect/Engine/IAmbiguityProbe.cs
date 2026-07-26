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

    // The candidate set the sentence stage may weigh for a word. When
    // includeTypedLiteral is true, the exact typed literal joins the set even if
    // it is not a French lexicon form, so the sentence stage can take back a
    // commit-stage diacritics correction from full context.
    IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral);
}
