namespace Deckle.Autocorrect;

// Runs an ordered list of correction policies and returns the first decision —
// the earlier policy wins. AutocorrectPolicySet owns the production order so the
// app, benchmark and tests cannot silently compose different engines; this type
// only enforces first-decision precedence and keeps each policy independently
// testable.
public sealed class CompositeCorrectionPolicy : ICorrectionPolicy
{
    private readonly ICorrectionPolicy[] _policies;

    public CompositeCorrectionPolicy(params ICorrectionPolicy[] policies) =>
        _policies = policies;

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext, CorrectionTrace? trace = null)
    {
        foreach (ICorrectionPolicy policy in _policies)
        {
            CorrectionDecision? decision = policy.Evaluate(word, leftContext, trace);
            if (decision is not null)
                return decision;
        }
        return null;
    }
}
