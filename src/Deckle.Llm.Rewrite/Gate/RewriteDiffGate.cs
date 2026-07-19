namespace Deckle.Llm.Rewrite;

// ─── The diff gate ───────────────────────────────────────────────────────────
//
// Mechanical validator of a paragraph rewrite, decided at the 2026-07-19
// framing: the model proposes, the gate decides whether the proposal can be
// offered at all. Global and all-or-nothing — either every edit of the diff
// is explained by one of the three rules (bounded-form replacement,
// closed-class insertion, duplicate/crutch deletion) under strictly
// monotone order, or the paragraph produces no offer. There is no partial
// application and no per-edit filtering (a noted door, closed in V1).
//
// Pure code by design: no OS calls, no EventSource, no state. The caller
// owns emission and consent — same division as SentenceCorpus in the
// autocorrect engine.
//
// Severity is the calibration stance: a false reject costs one offer, a
// false accept would cost the trust the corrector is built on. The two
// constants below are the gate's only knobs, and they are code-level —
// tuned against the offer/verdict dataset (tranche 4), never exposed as
// settings.

public static class RewriteDiffGate
{
    /// <summary>Absolute ceiling on the form distance of a replacement,
    /// whatever the word length.</summary>
    internal const int FormDistanceCap = 3;

    /// <summary>Relative bound: percent of the longer normalized form.
    /// 25 % lets "samarreter" → "sans m'arrêter" (distance 2 on 12) through
    /// and keeps "voiture" → "véhicule" (distance 5) out. Tightened from
    /// 34 % on the 2026-07-19 eval: at 34 %, "gate" → "gâteau" (distance 2
    /// on 6, a francization corruption) reached an offer.</summary>
    internal const int FormDistancePercent = 25;

    internal static int FormDistanceBound(int length)
        => Math.Min(FormDistanceCap, Math.Max(1, length * FormDistancePercent / 100));

    /// <summary>Rules on the diff between a typed paragraph and its rewrite.
    /// The verdict carries the full edit script either way — the offer shows
    /// it as-is, a rejection can always answer "why".</summary>
    public static DiffGateVerdict Evaluate(string original, string rewritten)
    {
        var a = GateTokenizer.Tokenize(original ?? "");
        var b = GateTokenizer.Tokenize(rewritten ?? "");
        return new DiffGateVerdict(DiffAlignment.Align(a, b));
    }
}
