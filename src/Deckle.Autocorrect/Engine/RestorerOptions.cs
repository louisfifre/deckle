namespace Deckle.Autocorrect;

// ── RestorerOptions ─────────────────────────────────────────────────────────
//
// The thresholds that govern when the restorer dares to act on an ambiguous
// folded form. Every default leans conservative: a wrongly corrected valid
// word is the worst outcome, so the bars sit high.
//
// Defaults are an engineer's starting point, to be grounded by the offline
// eval — not measured optima.
public sealed record RestorerOptions
{
    // Words shorter than this are never corrected — too little signal, and the
    // single-char class is blacklisted anyway.
    public int MinWordLength { get; init; } = 2;

    // Frequency dominance gate: the top candidate must out-frequency the runner
    // up by at least this ratio to win without context. 20× is steep on purpose.
    public double DominanceRatio { get; init; } = 20.0;

    // And the dominant candidate must itself be common enough — a rare word
    // winning a ratio over an even rarer one is not evidence.
    public double MinDominantFrequencyPerMillion { get; init; } = 1.0;

    // Floor under which a candidate is dropped from the pool entirely. 0 keeps
    // everything the index holds; raise it to prune corpus noise.
    public double MinCandidateFrequencyPerMillion { get; init; } = 0.0;

    // Sentence-stage rarity gate. A folded variant is dropped when its frequency
    // is below (reference frequency × this ratio) — i.e. more than 1/ratio rarer
    // than the reference. The reference is the typed literal's frequency when the
    // literal is a valid lexicon form; for a misspelled literal, which has no
    // frequency of its own, it falls back to the slot's most frequent lexicon
    // variant ("ca" → ça at 8 972/M anchors the slot, so çà at 21/M drops).
    // Replay of the ONNX sentence judge over the maintainer's real corpus
    // showed its wrong changes cluster on forms hundreds to thousands of times
    // rarer than the typed word (mais→maïs, le→lé, de→dé); a 100× floor (0.01)
    // removed 32 of 57 residual wrong changes on the clean subset while touching
    // none of the correct restorations, which sit at ratios of 0.09–0.5. The
    // commit-stage gate does not apply this; only the sentence stage does.
    public double MinCandidateFrequencyRatio { get; init; } = 0.01;

    // Eval-only mode: lets the pair model overturn a *valid* French form
    // (a→à, du→dû — the real-word class). Off in the live engine by doctrine;
    // the offline eval measures what it would buy and what it would break.
    public bool CorrectValidFormsWithContext { get; init; } = false;

    // Proper-noun guard: a title-cased word (leading capital, lowercase tail,
    // no internal capital) appearing mid-utterance is almost always a name —
    // Git, Azure — never a dictated French word, so its spelling is left alone.
    // Sentence-initial capitals are exempt: there a capital is the norm for a
    // word that may legitimately need an accent. Off by default — it trades a
    // few capitalized-accented restorations for killing the proper-noun class.
    public bool GuardCapitalizedMidSentence { get; init; } = false;
}
