namespace Deckle.Autocorrect;

// The first decision wins, so order is precedence. AutocorrectPolicySet owns the
// production order.
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
