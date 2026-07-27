namespace Deckle.Autocorrect;

// ── TypoOptions ──────────────────────────────────────────────────────────────
//
// The thresholds governing the typo corrector. Two tiers: a relaxed near tier
// (one edit away) for ordinary slips, and a strict far tier (two edits away) for
// bigger faults, tried only when nothing sits one edit away. The far tier is held
// to a higher word-length, frequency and dominance bar — a correction reaching
// further must clear stronger evidence. Aggression is the product (Louis: "taper
// à l'arrache et que ça réécrive"); the safety net is the correction inlay's
// undo, which writes a permanent suppression — until the inlay ships a wrong
// correction can only be re-edited by hand, so these bars stand on their own.
// Defaults are an engineer's operating point, tuned by feel, not measured optima.
public sealed record TypoOptions
{
    // Words shorter than this are never typo-corrected: a 1-2 letter non-word sits
    // one edit from too many valid words to ever be unambiguous. At 3 the corrector
    // reaches common short slips while a 2-letter intentional token stays safe.
    public int MinWordLength { get; init; } = 3;

    // The chosen near word must itself be common enough to be the obvious intent —
    // a rare neighbour winning a ratio over an even rarer one is not evidence.
    public double MinFrequencyPerMillion { get; init; } = 2.0;

    // With more than one near neighbour, the best must out-frequency the runner up
    // by at least this ratio to fire; a close second means real ambiguity.
    public double DominanceRatio { get; init; } = 5.0;

    // The farthest edit distance the corrector will reach. 1 keeps it to single
    // slips; 2 enables the far tier below for bigger faults ("bnjuor" → "bonjour").
    public int MaxEditDistance { get; init; } = 2;

    // The far tier (two edits) only applies to words at least this long: a short
    // word two edits away matches far too many valid words to be unambiguous.
    public int Edits2MinWordLength { get; init; } = 6;

    // The far tier's frequency floor — much higher than the near tier's. Reaching
    // two edits away, only a genuinely common target is credible.
    public double Edits2MinFrequencyPerMillion { get; init; } = 30.0;

    // The far tier's dominance ratio — stricter than the near tier's. At distance
    // two the valid neighbourhood is crowded, so the winner must clearly dominate.
    public double Edits2DominanceRatio { get; init; } = 12.0;

}
