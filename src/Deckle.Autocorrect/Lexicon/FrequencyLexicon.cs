using System.IO;
using System.IO.Compression;
using System.Text;

namespace Deckle.Autocorrect;

// ── FrequencyLexicon ────────────────────────────────────────────────────────
//
// An immutable form→frequency map, the raw material of both the lexical gate
// (which French/English forms exist) and the accent index (which forms carry
// diacritics, ranked by frequency). Frequency is occurrences per million on
// the Lexique scale.
//
// Forms are stored lowercased and NFC-normalized: the gate looks words up by
// their lowercased literal, so the store must agree on case and composition.
// A form appearing twice in the source keeps the SUM of its frequencies —
// merging variant rows of the same surface form is additive, never last-wins.
public sealed class FrequencyLexicon : IFrequencyLexicon
{
    private readonly Dictionary<string, double> _map;

    private FrequencyLexicon(Dictionary<string, double> map, int skippedLines)
    {
        _map = map;
        SkippedLines = skippedLines;
    }

    // Lines that could not be parsed (no tab, unparsable frequency). Skipped
    // silently but counted — a data-quality signal for build-data.
    public int SkippedLines { get; }

    public int Count => _map.Count;

    public bool Contains(string lowerForm) => _map.ContainsKey(lowerForm);

    // Frequency of a form, 0 when absent — absence is a zero, not an error.
    public double FrequencyOf(string lowerForm) =>
        _map.TryGetValue(lowerForm, out double f) ? f : 0.0;

    public IEnumerable<KeyValuePair<string, double>> Entries => _map;

    // Reads `form<TAB>frequency` lines, UTF-8. '#'-prefixed and blank lines are
    // comments; malformed lines are skipped (and counted). Duplicate forms sum.
    public static FrequencyLexicon LoadTsv(TextReader reader)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        int skipped = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                skipped++;
                continue;
            }

            string form = line[..tab];
            if (!double.TryParse(
                    line[(tab + 1)..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double freq))
            {
                skipped++;
                continue;
            }

            // Lowercase + NFC so lookups by lowercased literal always hit.
            string key = form.ToLowerInvariant().Normalize(NormalizationForm.FormC);
            map[key] = map.TryGetValue(key, out double prior) ? prior + freq : freq;
        }

        return new FrequencyLexicon(map, skipped);
    }

    // The shipped artifacts are gzip-compressed; decompress on the way in.
    public static FrequencyLexicon LoadTsvGz(string path)
    {
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return LoadTsv(reader);
    }
}
