using System.Globalization;
using System.Text;

namespace Deckle.Benchmark.CaptureTail;

// Word-level comparison of two transcripts. Returns numbers only: the words
// themselves never leave this class.
internal static class WordMetrics
{
    public static string[] Words(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    // Reference words that the hypothesis does not reproduce from its first
    // divergence on: the final words lost or changed. 0 when the hypothesis
    // keeps the whole reference as its prefix.
    public static int FinalWordsAffected(string[] reference, string[] hypothesis)
    {
        int common = 0;
        while (common < reference.Length && common < hypothesis.Length
               && reference[common] == hypothesis[common])
            common++;
        return reference.Length - common;
    }

    public static int EditDistance(string[] a, string[] b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int sub = prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), sub);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
