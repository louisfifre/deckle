using System.Globalization;
using System.Text;

namespace Deckle.Llm.Rewrite;

// ─── Gate tokenization ───────────────────────────────────────────────────────
//
// The diff gate compares two texts word by word, never character by character:
// its three rules (bounded-form replacement, closed-class insertion,
// duplicate/filler deletion) are all stated over words. Tokenization is
// therefore part of the gate's contract, not a detail — what counts as "one
// word" decides what counts as "one edit".
//
// Apostrophes glue to the word ("s'arrête", "aujourd'hui" are one token) so
// that an elision repair ("sarrete" → "s'arrête") reads as a single
// replacement whose normalized forms are equal. Hyphens glue only between
// letters ("peut-être") so a dash stays punctuation. Whitespace is dropped
// entirely: the rewrite may reflow the paragraph freely.
//
// Normalized form: lowercase, diacritics stripped, apostrophes and hyphens
// removed. Two tokens with equal normalized forms are the same word as far as
// trust is concerned — accent restoration, capitalization, re-elision are
// form repairs, not substitutions.

/// <summary>One word or punctuation run of a gated paragraph.</summary>
public readonly record struct GateToken(string Text, bool IsWord, string Normalized)
{
    /// <summary>True when any character is a digit — numbers are never
    /// allowed to drift, whatever the form distance says.</summary>
    public bool HasDigit
    {
        get
        {
            foreach (char c in Text)
                if (char.IsDigit(c)) return true;
            return false;
        }
    }
}

public static class GateTokenizer
{
    const char Apostrophe = '\'';
    const char CurlyApostrophe = '’';

    public static IReadOnlyList<GateToken> Tokenize(string text)
    {
        var tokens = new List<GateToken>();
        if (string.IsNullOrEmpty(text)) return tokens;

        int i = 0;
        int n = text.Length;
        var sb = new StringBuilder(24);

        while (i < n)
        {
            char c = text[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (IsWordChar(c))
            {
                sb.Clear();
                while (i < n)
                {
                    c = text[i];
                    if (IsWordChar(c)) { sb.Append(c); i++; continue; }
                    // Apostrophe after a word char stays in the word — elision
                    // ("j'", "s'arrête") and interior ("aujourd'hui") alike.
                    if (c is Apostrophe or CurlyApostrophe) { sb.Append(Apostrophe); i++; continue; }
                    // Hyphen only bridges two letters ("peut-être"); a trailing
                    // dash is punctuation.
                    if (c == '-' && i + 1 < n && IsWordChar(text[i + 1])) { sb.Append(c); i++; continue; }
                    break;
                }
                string word = sb.ToString();
                tokens.Add(new GateToken(word, IsWord: true, Normalize(word)));
                continue;
            }

            // Punctuation: a run of the same character is one token ("...",
            // "!!"), distinct characters are distinct tokens.
            char punct = c;
            int start = i;
            while (i < n && text[i] == punct) i++;
            string run = text[start..i];
            tokens.Add(new GateToken(run, IsWord: false, Normalize(run)));
        }

        return tokens;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>Lowercase, diacritics stripped, apostrophes and hyphens
    /// removed — the identity under which two forms are "the same word".</summary>
    public static string Normalize(string text)
    {
        string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is Apostrophe or CurlyApostrophe or '-') continue;
            // FormD leaves the French ligatures whole; fold them by hand so
            // "œuvre" and "oeuvre" are one form.
            if (c == 'œ') { sb.Append("oe"); continue; }
            if (c == 'æ') { sb.Append("ae"); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
