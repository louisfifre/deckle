using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect.Mlm;

// ISentenceReranker backed by the CamemBERT masked-LM. For one ambiguous slot it
// builds the left/right context from the sentence's other word-forms, masks the
// slot, and ranks the closed candidate set by fill-mask logit plus a frequency
// prior. It returns a form only when the top candidate clears a confidence
// margin over the runner-up — never a bare argmax, the same conservativity the
// rest of the engine enforces.
//
// The frequency prior shifts each candidate's score down by its log-frequency
// deficit against the most frequent candidate, scaled by freqPrior: the common
// form sits at zero, a rarer one must out-logit it by more than the deficit to
// win. This is "prefer the frequent form unless the model is really sure" — it
// stops the model picking the archaic « çà » over the everyday « ça » on the
// strength of a noisy context, the precision-first leak the real-prose eval
// surfaced. freqPrior = 0 recovers the pure-logit behaviour.
//
// v1 scope: candidates whose surface form is a SINGLE leading piece (the
// high-frequency function-word ambiguities — a/à, ou/où, du/dû…). If any
// candidate tokenizes to multiple pieces, the reranker abstains (returns null),
// leaving that slot to the gate — a multi-token PLL path is a later refinement.
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

    public string? Rerank(IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates)
    {
        // Each candidate must be a single leading piece, else abstain.
        var ids = new int[candidates.Count];
        for (int k = 0; k < candidates.Count; k++)
        {
            int id = _scorer.LeadingPieceId(candidates[k].Form.ToLowerInvariant());
            if (id < 0) return null;
            ids[k] = id;
        }

        string leftText = string.Join(' ', Slice(sentence, 0, slotIndex));
        string rightText = string.Join(' ', Slice(sentence, slotIndex + 1, sentence.Count));
        int[] left = _scorer.Encode(leftText, out _);
        int[] right = _scorer.Encode(rightText, out _);

        float[] logits = _scorer.MaskLogits(left, right);

        // The frequency prior is measured against the most frequent candidate,
        // so that form carries no penalty and rarer ones are pulled down.
        double logFreqMax = double.NegativeInfinity;
        for (int k = 0; k < candidates.Count; k++)
            logFreqMax = Math.Max(logFreqMax, Math.Log(Math.Max(candidates[k].FrequencyPerMillion, FreqFloor)));

        int bestK = 0;
        double best = double.NegativeInfinity, second = double.NegativeInfinity;
        for (int k = 0; k < ids.Length; k++)
        {
            double prior = _freqPrior *
                (Math.Log(Math.Max(candidates[k].FrequencyPerMillion, FreqFloor)) - logFreqMax);
            double s = logits[ids[k]] + prior;
            if (s > best) { second = best; best = s; bestK = k; }
            else if (s > second) second = s;
        }

        return best - second >= _margin ? candidates[bestK].Form : null;
    }

    private static IEnumerable<string> Slice(IReadOnlyList<string> list, int start, int end)
    {
        for (int i = start; i < end; i++)
            yield return list[i];
    }

    public void Dispose() => _scorer.Dispose();
}
