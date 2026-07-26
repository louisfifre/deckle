namespace Deckle.Autocorrect;

// Turns the closed-sentence scorer into the explicit KEEP-vs-proposal decision
// the future sentence stage needs. The original is always candidate zero: a
// proposal is accepted only when the scorer chooses it past its own calibrated
// margin. A plain top-1 mismatch is never enough to rewrite what the user typed.
public sealed class SentenceProposalVerifier
{
    private readonly ISentenceScorer _scorer;

    public SentenceProposalVerifier(ISentenceScorer scorer) => _scorer = scorer;

    public SentenceProposalVerification Verify(string original, string proposed)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(proposed))
            return SentenceProposalVerification.Abstained(
                SentenceProposalVerification.Reasons.EmptyText);

        if (string.Equals(original, proposed, StringComparison.Ordinal))
            return new SentenceProposalVerification(
                SentenceProposalVerdict.Keep,
                Array.Empty<SentenceCandidateScore>(),
                0.0,
                0.0,
                SentenceProposalVerification.Reasons.Identity);

        SentenceScoringOutcome outcome;
        try
        {
            outcome = _scorer.Score([original, proposed]);
        }
        catch
        {
            // Model/provider failure is an ordinary abstention at this boundary.
            // No exception may turn an optional sentence check into lost typing.
            return SentenceProposalVerification.Abstained(
                SentenceScoringOutcome.AbstainReasons.Error);
        }

        if (outcome.Chosen is not null
            && (!CarriesClosedScores(outcome.Scores, original, proposed)
                || !double.IsFinite(outcome.Margin)
                || !double.IsFinite(outcome.Threshold)
                || outcome.Threshold < 0.0
                || outcome.Margin < outcome.Threshold))
            return SentenceProposalVerification.Abstained(
                SentenceScoringOutcome.AbstainReasons.Error,
                outcome.Scores,
                outcome.Margin,
                outcome.Threshold);

        SentenceProposalVerdict verdict;
        string? reason = outcome.AbstainReason;
        if (outcome.Chosen is null)
        {
            verdict = SentenceProposalVerdict.Abstain;
        }
        else if (string.Equals(outcome.Chosen, original, StringComparison.Ordinal))
        {
            verdict = SentenceProposalVerdict.Keep;
        }
        else if (string.Equals(outcome.Chosen, proposed, StringComparison.Ordinal))
        {
            verdict = SentenceProposalVerdict.Accept;
        }
        else
        {
            // A scorer must choose from the closed set it received. Treat a
            // provider violating that contract as an abstention, never as text.
            verdict = SentenceProposalVerdict.Abstain;
            reason = SentenceScoringOutcome.AbstainReasons.Error;
        }

        return new SentenceProposalVerification(
            verdict,
            outcome.Scores,
            outcome.Margin,
            outcome.Threshold,
            reason);
    }

    private static bool CarriesClosedScores(
        IReadOnlyList<SentenceCandidateScore> scores,
        string original,
        string proposed) =>
        scores.Count == 2
        && string.Equals(scores[0].Text, original, StringComparison.Ordinal)
        && string.Equals(scores[1].Text, proposed, StringComparison.Ordinal)
        && double.IsFinite(scores[0].Score)
        && double.IsFinite(scores[1].Score);
}

public enum SentenceProposalVerdict
{
    Accept,
    Keep,
    Abstain,
}

public readonly record struct SentenceProposalVerification(
    SentenceProposalVerdict Verdict,
    IReadOnlyList<SentenceCandidateScore> Scores,
    double Margin,
    double Threshold,
    string? Reason)
{
    public static SentenceProposalVerification Abstained(
        string reason,
        IReadOnlyList<SentenceCandidateScore>? scores = null,
        double margin = 0.0,
        double threshold = 0.0) =>
        new(SentenceProposalVerdict.Abstain, scores ?? Array.Empty<SentenceCandidateScore>(), margin, threshold, reason);

    public static class Reasons
    {
        public const string EmptyText = "empty_text";
        public const string Identity = "identity";
    }
}
