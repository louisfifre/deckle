namespace Deckle.Autocorrect;

// Scores a closed set of full sentence candidates. The scorer never rewrites or
// invents text: callers provide every candidate that may be chosen.
public interface ISentenceScorer
{
    SentenceScoringOutcome Score(IReadOnlyList<string> candidates);
}

public readonly record struct SentenceScoringOutcome(
    string? Chosen,
    IReadOnlyList<SentenceCandidateScore> Scores,
    double Margin,
    double Threshold,
    string? AbstainReason)
{
    public static SentenceScoringOutcome Abstained(string reason) =>
        new(null, Array.Empty<SentenceCandidateScore>(), 0.0, 0.0, reason);

    public static class AbstainReasons
    {
        public const string NoCandidates      = "no_candidates";
        public const string SingleCandidate   = "single_candidate";
        public const string EmptyCandidate    = "empty_candidate";
        public const string TooFewTokens      = "too_few_tokens";
        public const string VocabSizeMissing  = "vocab_size_missing";
        public const string TokenOutOfVocab   = "token_out_of_vocab";
        public const string LogitsUnavailable = "logits_unavailable";
        public const string BelowMargin       = "below_margin";
        public const string Error             = "error";
    }
}

public readonly record struct SentenceCandidateScore(
    string Text,
    double Score,
    double LogProbability,
    int ScoredTokenCount);
