namespace Deckle.Autocorrect;

// Runs an ordered list of correction policies and returns the first decision —
// the earlier policy wins. The live engine chains the diacritics gate (valid
// forms, accents) ahead of the conservative typo corrector (non-words): the two
// are disjoint by construction — the gate yields null on a non-word, the typo
// corrector refuses any valid form — but the order makes the precedence explicit
// and keeps each policy a single, testable responsibility.
public sealed class CompositeCorrectionPolicy : ICorrectionPolicy
{
    private readonly ICorrectionPolicy[] _policies;

    public CompositeCorrectionPolicy(params ICorrectionPolicy[] policies) =>
        _policies = policies;

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext)
    {
        foreach (ICorrectionPolicy policy in _policies)
        {
            CorrectionDecision? decision = policy.Evaluate(word, leftContext);
            if (decision is not null)
                return decision;
        }
        return null;
    }
}
