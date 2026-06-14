namespace Deckle.Input.Autocorrect;

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

    // The English guard is a frequency bar, not a membership test: the EN
    // lexicon (web counts) is polluted with bare-stripped French ("francais",
    // "ecole"), so only a form actually frequent in English blocks correction.
    public double EnglishGuardMinPerMillion { get; init; } = 5.0;

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
