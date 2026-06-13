using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Lexicon;

namespace Deckle.Input.Autocorrect.Cli.Mlm;

// ISentenceReranker backed by the CamemBERT masked-LM. For one ambiguous slot it
// builds the left/right context from the sentence's other word-forms, masks the
// slot, and ranks the closed candidate set by fill-mask logit. It returns a form
// only when the top candidate clears a confidence margin over the runner-up —
// never a bare argmax, the same conservativity the rest of the engine enforces.
//
// v1 scope: candidates whose surface form is a SINGLE leading piece (the
// high-frequency function-word ambiguities — a/à, ou/où, du/dû…). If any
// candidate tokenizes to multiple pieces, the reranker abstains (returns null),
// leaving that slot to the gate — a multi-token PLL path is a later refinement.
internal sealed class CamembertSentenceReranker : ISentenceReranker, IDisposable
{
    private readonly CamembertMlmScorer _scorer;
    private readonly double _margin;

    public CamembertSentenceReranker(string modelDir, double margin)
    {
        _scorer = new CamembertMlmScorer(modelDir);
        _margin = margin;
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

        int bestK = 0;
        float best = float.NegativeInfinity, second = float.NegativeInfinity;
        for (int k = 0; k < ids.Length; k++)
        {
            float s = logits[ids[k]];
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
