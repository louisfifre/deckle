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

    // Of the reference's last `count` words, how many are not found (each word
    // used once) among the hypothesis's last `count` + 2 words. A tail that cut
    // the end of the speech shows here; a difference earlier in the text does not.
    public static int FinalWordsMissing(string[] reference, string[] hypothesis, int count)
    {
        var pool = hypothesis.Skip(Math.Max(0, hypothesis.Length - (count + 2))).ToList();
        int missing = 0;
        foreach (string w in reference.Skip(Math.Max(0, reference.Length - count)))
        {
            if (!pool.Remove(w)) missing++;
        }
        return missing;
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
