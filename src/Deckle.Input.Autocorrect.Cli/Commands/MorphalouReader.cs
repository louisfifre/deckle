using System.IO;

namespace Deckle.Input.Autocorrect.Cli;

// Reads inflected surface forms from the Morphalou 3.1 "tout en un" CSV
// (semicolon-separated, UTF-8). The file opens with a free-text preamble, a
// group line (LEMME;…;FLEXION;…), then a column header carrying TWO "GRAPHIE"
// columns: the first is the lemma graphie, the second (the FLEXION block) is
// the inflected surface form we want. We locate that column from the header
// rather than hard-coding an index, so a preamble of a different length cannot
// silently misread.
public static class MorphalouReader
{
    // Yields each inflected surface form, raw and unfiltered (the caller applies
    // the accepted-form filter and the merge). Throws if the column header is
    // never found — a clear signal the format drifted.
    public static IEnumerable<string> ReadInflectedForms(TextReader reader)
    {
        int formColumn = LocateInflectedFormColumn(reader);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;

            string[] fields = line.Split(';');
            if (fields.Length <= formColumn)
                continue;

            string form = fields[formColumn];
            if (form.Length > 0)
                yield return form;
        }
    }

    // Consumes the preamble up to and including the column-header line, and
    // returns the index of the inflected-form column — the SECOND "GRAPHIE"
    // (the first is the lemma). The header is the first line that carries two
    // distinct "GRAPHIE" fields. Throws if none is seen.
    private static int LocateInflectedFormColumn(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string[] fields = line.Split(';');
            int first = Array.IndexOf(fields, "GRAPHIE");
            int last = Array.LastIndexOf(fields, "GRAPHIE");
            if (first >= 0 && last > first)
                return last;
        }

        throw new InvalidOperationException(
            "Morphalou header not found: no line carries two 'GRAPHIE' columns.");
    }
}
