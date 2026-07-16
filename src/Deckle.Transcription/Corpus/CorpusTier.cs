namespace Deckle.Transcription;

// Five length tiers applied to ASR output, indexed on word count. Hardcoded
// here because the later Settings pass will decide whether these thresholds
// deserve user editing; until then, the centralization point exists and
// tier-stratified corpus analyses remain consistent.
//
// Bounds: floor included, ceiling excluded. Empty text falls into very-short.
//
//   very-short → 0   ≤ words < 30
//   short      → 30  ≤ words < 200
//   medium     → 200 ≤ words < 1000
//   long       → 1000 ≤ words < 3000
//   very-long  → 3000 ≤ words
internal static class CorpusTier
{
    public const int ShortLowerBound    = 30;
    public const int MediumLowerBound   = 200;
    public const int LongLowerBound     = 1000;
    public const int VeryLongLowerBound = 3000;

    public static string Resolve(int wordCount)
    {
        if (wordCount < ShortLowerBound)    return "very-short";
        if (wordCount < MediumLowerBound)   return "short";
        if (wordCount < LongLowerBound)     return "medium";
        if (wordCount < VeryLongLowerBound) return "long";
        return "very-long";
    }
}
