using System.Globalization;
using System.Text;

namespace Deckle.Autocorrect;

// ── AccentFolding ───────────────────────────────────────────────────────────
//
// The canonical key of the whole engine: a folded form collapses case and
// diacritics so every accented variant of a word lands under one lookup key
// (été, étè, ete → "ete"). Folding is what lets a QWERTY-US typist's bare
// "ete" reach the accented candidates indexed behind it.
//
// Folding = lowercase + strip combining marks (NFD, drop NonSpacingMark, back
// to NFC) + the two French ligatures œ/æ.
//
// Fold is on the hot path, one call per committed word. A word that needs no
// folding (plain ASCII, already lowercase) is returned without touching the
// normalizer, which is the expensive part.
public static class AccentFolding
{
    public static string Fold(string s)
    {
        if (!NeedsFold(s, out bool needsLigature))
            return s;

        string ligatured = needsLigature ? FoldLigatures(s) : s;
        return StripAndLower(ligatured);
    }

    // Case-preserving fold: strips diacritics and ligatures but keeps case.
    // Models what a QWERTY-US typist produces from an accented reference
    // (É → E, not e) — used by the offline eval to synthesize inputs.
    public static string StripDiacritics(string s)
    {
        if (!NeedsStrip(s, out bool needsLigature))
            return s;

        string ligatured = needsLigature ? FoldLigaturesPreservingCase(s) : s;
        return StripMarks(ligatured);
    }

    public static bool HasDiacritics(string s) => StripDiacritics(s) != s;

    private static bool NeedsFold(string s, out bool needsLigature)
    {
        needsLigature = false;
        bool needsWork = false;
        foreach (char c in s)
        {
            if (c is 'œ' or 'Œ' or 'æ' or 'Æ')
            {
                needsLigature = true;
                needsWork = true;
            }
            else if (c > 127 || char.IsUpper(c))
            {
                // Non-ASCII may decompose to a base + mark; uppercase must lower.
                needsWork = true;
            }
        }
        return needsWork;
    }

    private static bool NeedsStrip(string s, out bool needsLigature)
    {
        needsLigature = false;
        bool needsWork = false;
        foreach (char c in s)
        {
            if (c is 'œ' or 'Œ' or 'æ' or 'Æ')
            {
                needsLigature = true;
                needsWork = true;
            }
            else if (c > 127)
            {
                needsWork = true;
            }
        }
        return needsWork;
    }

    private static string StripAndLower(string s)
    {
        string decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string StripMarks(string s)
    {
        string decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Ligatures have no decomposition — expand them by hand before NFD.
    private static string FoldLigatures(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        foreach (char c in s)
        {
            switch (c)
            {
                case 'œ' or 'Œ': sb.Append("oe"); break;
                case 'æ' or 'Æ': sb.Append("ae"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // The eval's typist keeps the leading capital: Œ → Oe.
    private static string FoldLigaturesPreservingCase(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        foreach (char c in s)
        {
            switch (c)
            {
                case 'œ': sb.Append("oe"); break;
                case 'Œ': sb.Append("Oe"); break;
                case 'æ': sb.Append("ae"); break;
                case 'Æ': sb.Append("Ae"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
