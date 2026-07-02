namespace Deckle.Autocorrect;

// Minimal lexicon capability consumed by correction policies. The concrete
// artifact may stay French today; the policy contract only needs membership and
// frequency so the primary language can be swapped without rewriting the chain.
public interface IFrequencyLexicon
{
    bool Contains(string lowerForm);

    double FrequencyOf(string lowerForm);
}
