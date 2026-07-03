using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Onnx;

// Adapts a closed-sentence ISentenceScorer (the ONNX judge) to the slot-level
// ISentenceReranker the sentence stage speaks. For one ambiguous slot it splices
// each candidate surface form into the sentence, scores the resulting full
// sentences, and remaps the winner back to the slot's chosen form. It stays a
// CORRECTION, not a rewrite: the verdict is always one of the caller's candidate
// forms or an abstention, never invented text.
//
// The judge is a full-sentence forced-decoding scorer — seconds per slot on CPU
// int4 — so this reranker is meant for an observing role (shadow telemetry, an
// offline replay over the collected corpus), not a synchronous hot-path stage.
public sealed class OnnxSlotReranker : ISentenceReranker, IDisposable
{
    private readonly ISentenceScorer _scorer;
    private readonly bool _ownsScorer;

    // ownsScorer governs disposal: true when this reranker created the scorer
    // (TryLoad), false when a caller injected a shared one it owns itself.
    public OnnxSlotReranker(ISentenceScorer scorer, bool ownsScorer = true)
    {
        _scorer = scorer;
        _ownsScorer = ownsScorer;
    }

    // Stages the ONNX judge from a model directory; null when the model is absent
    // or fails to load, so a caller can fall back to another reranker or none.
    public static OnnxSlotReranker? TryLoad(string modelDir, double margin)
    {
        ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(modelDir, margin);
        return scorer is null ? null : new OnnxSlotReranker(scorer);
    }

    public RerankOutcome Rerank(
        IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates)
    {
        if (candidates.Count == 0)
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule);
        if (slotIndex < 0 || slotIndex >= sentence.Count)
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.Error);

        // One full-sentence candidate per surface form, in candidate order: the
        // slot word is swapped, the rest of the sentence held fixed.
        var words = new string[sentence.Count];
        for (int w = 0; w < sentence.Count; w++)
            words[w] = sentence[w];

        var sentences = new string[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            words[slotIndex] = candidates[i].Form;
            sentences[i] = string.Join(' ', words);
        }

        SentenceScoringOutcome outcome = _scorer.Score(sentences);

        // The scorer preserves candidate order, so per-sentence scores map straight
        // back to per-form scores. It may return fewer scores than candidates on an
        // early abstention — take the pairs it did produce.
        int scored = Math.Min(candidates.Count, outcome.Scores.Count);
        var scores = new RerankCandidateScore[scored];
        for (int i = 0; i < scored; i++)
            scores[i] = new RerankCandidateScore(candidates[i].Form, outcome.Scores[i].Score);

        // The chosen full sentence maps back to its slot form by position.
        string? chosenForm = null;
        if (outcome.Chosen is not null)
        {
            int idx = Array.IndexOf(sentences, outcome.Chosen);
            if (idx >= 0)
                chosenForm = candidates[idx].Form;
        }

        return new RerankOutcome(
            chosenForm,
            scores,
            outcome.Margin,
            outcome.Threshold,
            // Surface the judge's own abstain reason for observation; fall back to
            // the reranker vocabulary when the verdict landed but did not map back.
            chosenForm is not null
                ? null
                : outcome.AbstainReason ?? RerankOutcome.AbstainReasons.NoRule);
    }

    public void Dispose()
    {
        if (_ownsScorer && _scorer is IDisposable disposable)
            disposable.Dispose();
    }
}
