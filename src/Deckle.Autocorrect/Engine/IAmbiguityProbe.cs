using Deckle.Autocorrect;

namespace Deckle.Autocorrect;

// Lets a caller ask the gate which words are genuinely ambiguous slots — those
// whose folded key carries two or more surface forms a context model must
// resolve (a/à, ou/où, cote/côté). A bare null from Evaluate does not say this:
// it conflates "left a valid literal", "blacklisted", and "ambiguous, deferred".
// The reranker needs the closed candidate set, and only for the real residue.
public interface IAmbiguityProbe
{
    // The closed candidate set for a word when its fold holds >=2 surface forms
    // (after the frequency floor and user suppressions); empty when there is
    // nothing to disambiguate. Used for a commit the gate left literal.
    IReadOnlyList<AccentVariant> AmbiguousCandidates(string word);

    // The candidate set the sentence stage may weigh for a word. When
    // includeTypedLiteral is true, the exact typed literal joins the set even if
    // it is not a French lexicon form, so the sentence stage can take back a
    // commit-stage diacritics correction from full context.
    IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral);
}
