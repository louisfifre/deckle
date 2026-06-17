namespace Deckle.Autocorrect;

// ── TypoOptions ──────────────────────────────────────────────────────────────
//
// The thresholds governing the conservative typo corrector. Like RestorerOptions
// every default leans safe: a non-word is only spell-fixed when one common French
// word sits a single edit away and clearly dominates any rival. Defaults are an
// engineer's starting point, to be grounded by the offline eval — not measured
// optima.
public sealed record TypoOptions
{
    // Words shorter than this are never typo-corrected: a 2-3 letter non-word
    // sits one edit from too many valid words to ever be unambiguous, and the
    // risk of mangling an intentional short token is high. Diacritics still
    // covers short accent restorations (its own MinWordLength is lower).
    public int MinWordLength { get; init; } = 4;

    // The chosen word must itself be common enough to be the obvious intent —
    // a rare neighbour winning a ratio over an even rarer one is not evidence.
    public double MinFrequencyPerMillion { get; init; } = 5.0;

    // With more than one valid neighbour, the best must out-frequency the runner
    // up by at least this ratio to fire; a close second means real ambiguity, so
    // the literal stays.
    public double DominanceRatio { get; init; } = 10.0;

    // Bilingual guard: a non-word that is itself frequent English is left alone
    // rather than frenchified. Inert while the live engine runs French-only
    // (english lexicon null), but correct for the bilingual path.
    public double EnglishGuardMinPerMillion { get; init; } = 5.0;
}
