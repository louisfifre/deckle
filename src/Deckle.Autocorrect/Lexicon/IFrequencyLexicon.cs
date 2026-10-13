namespace Deckle.Autocorrect;

// Minimal lexicon capability consumed by correction policies: membership and
// frequency only, so the primary language can be swapped without rewriting the
// chain.
public interface IFrequencyLexicon
{
    bool Contains(string lowerForm);

    double FrequencyOf(string lowerForm);
}
