using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Onnx;

// Adapts a closed-sentence ISentenceScorer (the ONNX judge) to the slot-level
// ISentenceReranker the sentence stage speaks. For one ambiguous slot it splices
// each candidate surface form into the sentence, scores the resulting full
// sentences, and remaps the winner back to the slot's chosen form. It stays a
// CORRECTION, not a rewrite: the verdict is always one of the caller's candidate
// forms or an abstention, never invented text.
//
// The judge is a full-sentence forced-decoding scorer whose speed follows the
// execution provider it loads onto: seconds per slot on CPU int4 (offline-only),
// ~0.6 s on the GPU via DirectML — live-viable behind the engine's background
// rerank lane, which keeps inference off the input thread, holds one request in
// flight and drops stale verdicts by epoch. The offline replay drives the same
// class, so live and replay inherit one behavior.
public sealed class OnnxSlotReranker : ISentenceReranker, IDisposable
{
    // Context floor: the judge is unreliable on short sentences. Replayed over the
    // maintainer's corpus its changes-only precision was 33% on 1–3-word sentences
    // and 39% on 4–6 words, against 55–65% at 7+ — the wrong changes being
    // sentence-initial imperatives it reads as participles (continue→continué),
    // where margin does not separate the two but sentence length does. Below four
    // word tokens the judge abstains outright. The floor lives here, the choke
    // point shared by the live stage and the offline replay, so both inherit it.
    private const int MinSentenceWordTokens = 4;

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
    public static OnnxSlotReranker? TryLoad(string modelDir, double margin, string executionProvider = "dml")
    {
        ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(modelDir, margin, executionProvider);
        return scorer is null ? null : new OnnxSlotReranker(scorer);
    }

    public RerankOutcome Rerank(
        IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates)
    {
        if (candidates.Count == 0)
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule);
        if (slotIndex < 0 || slotIndex >= sentence.Count)
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.Error);

        // Context floor (see MinSentenceWordTokens): a sentence carrying too few
        // word tokens does not give the judge enough to be trusted, so abstain
        // before scoring. Word tokens are the entries that carry a letter — the
        // sentence is a list of output word-forms, punctuation living on the
        // boundary rather than as its own token.
        int wordTokens = 0;
        foreach (string token in sentence)
        {
            foreach (char c in token)
                if (char.IsLetter(c))
                {
                    wordTokens++;
                    break;
                }
        }
        if (wordTokens < MinSentenceWordTokens)
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.ShortContext);

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
