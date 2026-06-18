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
// to NFC) + the two French ligatures œ/æ that no decomposition splits.
//
// Fold is on the hot path — one call per committed word. The common case is a
// word that needs no folding at all (plain ASCII, already lowercase): for that
// we return the lowercased input without ever touching the normalizer, which
// is the expensive part.
public static class AccentFolding
{
    // Lowercased canonical key — case-folded and diacritic-stripped.
    public static string Fold(string s)
    {
        // Fast path: scan once. If nothing needs folding (no uppercase, no
        // mark-bearing char, no ligature) the lowercased string is the answer
        // and we skip NFD/NFC entirely.
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

    // True when stripping diacritics would change the string.
    public static bool HasDiacritics(string s) => StripDiacritics(s) != s;

    // Detects whether Fold has any work to do: a non-lowercase char, a char
    // that carries (or could carry) a combining mark, or a ligature.
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

    // Like NeedsFold but case is preserved, so uppercase alone is not work.
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

    // NFD decomposition splits an accented letter into base + combining mark;
    // dropping the marks and lowering yields the folded key, recomposed to NFC.
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

    // Same mark-stripping, case left intact.
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

    // Case-preserving ligature expansion: Œ → Oe, œ → oe (the eval's typist
    // keeps the leading capital).
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
