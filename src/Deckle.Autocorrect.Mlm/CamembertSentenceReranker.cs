using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Mlm;

// ISentenceReranker backed by the CamemBERT masked-LM. For one ambiguous slot it
// builds the left/right context from the sentence's other word-forms, masks the
// slot, and ranks the closed candidate set by fill-mask logit plus a frequency
// prior. It returns a form only when the top candidate clears a confidence
// margin over the runner-up, never a bare argmax.
//
// The frequency prior shifts each candidate's score down by its log-frequency
// deficit against the most frequent candidate, scaled by freqPrior: the common
// form sits at zero, a rarer one must out-logit it by more than the deficit to
// win. It stops the model picking the archaic « çà » over the everyday « ça »
// on the strength of a noisy context. freqPrior = 0 recovers the pure-logit
// behaviour.
//
// Only candidates whose surface form is a SINGLE leading piece are scored (the
// function-word ambiguities: a/à, ou/où, du/dû…). If any candidate tokenizes to
// several pieces, the reranker abstains and the slot stays with the gate.
internal sealed class CamembertSentenceReranker : ISentenceReranker, IDisposable
{
    // Frequency floor (ppm) so the log of a form absent from the lexicon stays
    // finite — a near-zero prior weight, not a true count.
    private const double FreqFloor = 0.01;

    private readonly CamembertMlmScorer _scorer;
    private readonly double _margin;
    private readonly double _freqPrior;

    public CamembertSentenceReranker(string modelDir, double margin, double freqPrior)
    {
        _scorer = new CamembertMlmScorer(modelDir);
        _margin = margin;
        _freqPrior = freqPrior;
    }

    public RerankOutcome Rerank(IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates)
    {
        var ids = new int[candidates.Count];
        for (int k = 0; k < candidates.Count; k++)
        {
            int id = _scorer.LeadingPieceId(candidates[k].Form.ToLowerInvariant());
            if (id < 0) return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.MultiToken);
            ids[k] = id;
        }

        string leftText = string.Join(' ', Slice(sentence, 0, slotIndex));
        string rightText = string.Join(' ', Slice(sentence, slotIndex + 1, sentence.Count));
        int[] left = _scorer.Encode(leftText, out _);
        int[] right = _scorer.Encode(rightText, out _);

        float[] logits = _scorer.MaskLogits(left, right);

        double logFreqMax = double.NegativeInfinity;
        for (int k = 0; k < candidates.Count; k++)
            logFreqMax = Math.Max(logFreqMax, Math.Log(Math.Max(candidates[k].FrequencyPerMillion, FreqFloor)));

        var scores = new RerankCandidateScore[candidates.Count];
        int bestK = 0;
        double best = double.NegativeInfinity, second = double.NegativeInfinity;
        for (int k = 0; k < ids.Length; k++)
        {
            double prior = _freqPrior *
                (Math.Log(Math.Max(candidates[k].FrequencyPerMillion, FreqFloor)) - logFreqMax);
            double s = logits[ids[k]] + prior;
            scores[k] = new RerankCandidateScore(candidates[k].Form, s);
            if (s > best) { second = best; best = s; bestK = k; }
            else if (s > second) second = s;
        }

        double margin = best - second;
        bool cleared = margin >= _margin;
        return new RerankOutcome(
            cleared ? candidates[bestK].Form : null,
            scores,
            margin,
            _margin,
            cleared ? null : RerankOutcome.AbstainReasons.BelowMargin);
    }

    private static IEnumerable<string> Slice(IReadOnlyList<string> list, int start, int end)
    {
        for (int i = start; i < end; i++)
            yield return list[i];
    }

    public void Dispose() => _scorer.Dispose();
}
