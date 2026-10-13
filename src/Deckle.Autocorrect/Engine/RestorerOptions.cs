namespace Deckle.Autocorrect;

// The thresholds that govern when the restorer dares to act on an ambiguous
// folded form. Every default leans conservative: a wrongly corrected valid
// word is the worst outcome, so the bars sit high. Defaults are an engineer's
// starting point, not measured optima.
public sealed record RestorerOptions
{
    // Words shorter than this are never corrected: too little signal.
    public int MinWordLength { get; init; } = 2;

    // Frequency dominance gate: the top candidate must out-frequency the runner
    // up by at least this ratio to win without context. Steep on purpose.
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
    // variant ("ca" → ça anchors the slot, so the far rarer çà drops). The
    // sentence judge's wrong changes cluster on forms far rarer than the typed
    // word (mais→maïs, le→lé, de→dé), while correct restorations sit much closer
    // to it. Only the sentence stage applies this gate, never the commit stage.
    public double MinCandidateFrequencyRatio { get; init; } = 0.01;

    // Eval-only mode: lets the pair model overturn a *valid* French form
    // (a→à, du→dû — the real-word class). Off in the live engine; the offline
    // eval measures what it would buy and what it would break.
    public bool CorrectValidFormsWithContext { get; init; } = false;

    // Proper-noun guard: a title-cased word (leading capital, lowercase tail,
    // no internal capital) appearing mid-utterance is almost always a name —
    // Git, Azure — never a dictated French word, so its spelling is left alone.
    // Sentence-initial capitals are exempt: there a capital is the norm for a
    // word that may legitimately need an accent. Off by default — it trades a
    // few capitalized-accented restorations for killing the proper-noun class.
    public bool GuardCapitalizedMidSentence { get; init; } = false;
}
