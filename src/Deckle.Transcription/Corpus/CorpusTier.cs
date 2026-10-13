namespace Deckle.Transcription;

// Five length tiers applied to ASR output, indexed on word count. The single
// definition keeps tier-stratified corpus analyses consistent. Empty text falls
// into very-short.
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
