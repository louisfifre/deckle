using Deckle.Autocorrect;

namespace Deckle.Autocorrect;

// A post-sentence, bidirectional disambiguator: unlike IPairDisambiguator (which
// sees only the left context, word by word), this one runs once the sentence is
// complete and weighs the FULL context — words on both sides of the slot. It is
// still a CORRECTION, not a rewrite: it only ranks the closed set of surface
// forms the lexicon already proposed, and never invents one.
public interface ISentenceReranker
{
    // Given the sentence as ordered output word-forms and the index of one
    // ambiguous slot within it, weigh the closed candidate set and report the
    // verdict: the chosen surface form (only when the model clears its confidence
    // margin, else null — the conservative default) together with the per-candidate
    // scores and the margin it cleared or missed. The scores ride back to the
    // coordinator's decision telemetry, the confidence gauge of the last stage.
    RerankOutcome Rerank(IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates);
}

// Optional capability implemented by judges that can arbitrate the complete
// closed candidate set in one transaction. Legacy masked-LM implementations can
// remain slot-based; the coordinator falls back only for candidate families that
// already had slot-local rights.
public interface IWholeSentenceReranker
{
    RerankOutcome RerankSentence(ClosedSentenceTransaction transaction);
}

// The reranker's verdict for one slot, scores included. Chosen is the winning
// surface form or null to leave the slot; Scores are every candidate's final
// score (fill-mask logit plus frequency prior), highest = most favoured; Margin
// is the top-vs-second gap and Threshold the bar it had to clear; AbstainReason
// names why a null verdict happened.
public readonly record struct RerankOutcome(
    string? Chosen,
    IReadOnlyList<RerankCandidateScore> Scores,
    double Margin,
    double Threshold,
    string? AbstainReason)
{
    // Set only by a whole-sentence verdict. Chosen remains the replacement form;
    // this identifies the one slot whose one-edit sentence won globally.
    public int? ChosenSlotIndex { get; init; }

    // Convenience for the abstain paths that weigh nothing (e.g. a multi-token
    // candidate the v1 reranker cannot score).
    public static RerankOutcome Abstained(string reason) =>
        new(null, System.Array.Empty<RerankCandidateScore>(), 0.0, 0.0, reason);

    // Closed vocabulary of abstain reasons.
    public static class AbstainReasons
    {
        public const string MultiToken   = "multi_token";   // a candidate is not a single leading piece
        public const string BelowMargin  = "below_margin";  // top did not clear the confidence margin
        public const string NoRule       = "no_rule";       // no deterministic rule or model handled the slot
        public const string ShortContext = "short_context"; // too few word tokens for the judge to be reliable
        public const string CandidateOverflow = "candidate_overflow";
        public const string StaleEvidence = "stale_evidence"; // verified caret text changed before apply
        public const string WholeSentenceUnsupported = "whole_sentence_unsupported";
        public const string Error        = "error";         // inference threw — abstained, lane survives
    }
}

// One candidate's final reranker score.
public readonly record struct RerankCandidateScore(string Form, double Score);
