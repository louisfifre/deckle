using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

public static partial class LexiconBuilder
{
    // ── English: Norvig count_1w ───────────────────────────────────────────
    //
    // `word<TAB>count`. ppm = count / total * 1e6 on the same per-million scale
    // as Lexique, so the English guard and the French frequencies are
    // comparable. Only pure a-z words are kept — the guard is about plain
    // English forms colliding with bare-stripped French.
    private static void BuildEnglish(string sourcePath, string outPath)
    {
        var counts = new Dictionary<string, double>(StringComparer.Ordinal);
        double total = 0.0;
        long skipped = 0;

        using (var reader = new StreamReader(sourcePath, Encoding.UTF8))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0) { skipped++; continue; }

                string word = line[..tab];
                if (!IsPureAsciiLower(word)) { skipped++; continue; }
                if (!double.TryParse(line[(tab + 1)..], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double count))
                { skipped++; continue; }

                counts[word] = counts.TryGetValue(word, out double prior) ? prior + count : count;
                total += count;
            }
        }

        var ppm = new Dictionary<string, double>(counts.Count, StringComparer.Ordinal);
        if (total > 0.0)
            foreach (var (word, count) in counts)
                ppm[word] = count / total * 1_000_000.0;

        WriteLexicon(outPath, ppm);

        Console.WriteLine($"English: kept {ppm.Count:N0} words ({skipped:N0} skipped).");
    }
}
