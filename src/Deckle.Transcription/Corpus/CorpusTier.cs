namespace Deckle.Transcription.Corpus;

// Cinq tiers de longueur appliqués à la sortie ASR, indexés sur le
// word count. Hard-codés ici parce que la passe Settings ultérieure
// décidera si ces seuils méritent d'être édités par l'utilisateur —
// d'ici là, le point de centralisation existe et les analyses tier-
// stratifiées du corpus restent cohérentes. Voir ADR-0011.
//
// Bornes : plancher inclus, plafond exclu. Un texte vide tombe dans
// very-short.
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
