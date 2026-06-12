using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Deckle.Input.Autocorrect.Lexicon;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// Regenerates the derived lexicons from the raw sources. French from
// Lexique 3.83 (form + film/book frequencies), English from Norvig's
// count_1w (the guard). Both land as `form<TAB>freq` gzip TSVs, ordinally
// sorted so the artifact is byte-deterministic across runs and machines.
internal static class BuildDataCommand
{
    public static int Run(CliArgs args)
    {
        string root = RepoPaths.RepoRoot();
        string rawDir = args.ValueOr("--raw", RepoPaths.DefaultRawDir(root));
        string outDir = args.ValueOr("--out", RepoPaths.DefaultDataDir(root));

        Directory.CreateDirectory(outDir);

        string frenchOut = DataSet.FrenchPath(outDir);
        string englishOut = DataSet.EnglishPath(outDir);

        Console.WriteLine($"Raw  : {rawDir}");
        Console.WriteLine($"Out  : {outDir}");
        Console.WriteLine();

        BuildFrench(Path.Combine(rawDir, "Lexique383.tsv"), frenchOut);
        BuildEnglish(Path.Combine(rawDir, "count_1w.txt"), englishOut);

        SelfCheck(frenchOut, englishOut);
        return 0;
    }

    // ── French: Lexique 3.83 ───────────────────────────────────────────────
    //
    // The header names the columns; we resolve ortho/freqfilms2/freqlivres by
    // name so a column reorder upstream cannot silently misread. Per ortho we
    // sum the film and book frequencies separately across its rows (the same
    // surface appears once per lemma/POS), then take the max of the two sums —
    // the more generous of the two registers. Multi-word and junk forms are
    // dropped by the letter/apostrophe/hyphen filter.
    private static void BuildFrench(string sourcePath, string outPath)
    {
        using var reader = new StreamReader(sourcePath, Encoding.UTF8);

        string? header = reader.ReadLine()
            ?? throw new InvalidOperationException($"Empty Lexique file: {sourcePath}");
        string[] cols = header.Split('\t');
        int iOrtho = IndexOf(cols, "ortho");
        int iFilms = IndexOf(cols, "freqfilms2");
        int iLivres = IndexOf(cols, "freqlivres");

        var sumFilms = new Dictionary<string, double>(StringComparer.Ordinal);
        var sumLivres = new Dictionary<string, double>(StringComparer.Ordinal);

        long rows = 0, skippedForm = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            string[] f = line.Split('\t');
            if (f.Length <= iLivres) continue;

            string ortho = f[iOrtho];
            if (!IsAcceptedForm(ortho)) { skippedForm++; continue; }

            double films = ParseFreq(f[iFilms]);
            double livres = ParseFreq(f[iLivres]);

            sumFilms[ortho] = sumFilms.TryGetValue(ortho, out double pf) ? pf + films : films;
            sumLivres[ortho] = sumLivres.TryGetValue(ortho, out double pl) ? pl + livres : livres;
            rows++;
        }

        // Final frequency per form = max of the two register sums.
        var final = new Dictionary<string, double>(sumFilms.Count, StringComparer.Ordinal);
        foreach (var (form, films) in sumFilms)
            final[form] = Math.Max(films, sumLivres.GetValueOrDefault(form, 0.0));

        WriteLexicon(outPath, final);

        Console.WriteLine($"French: kept {final.Count:N0} forms from {rows:N0} rows "
                        + $"({skippedForm:N0} forms filtered out).");
    }

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

    // ── Shared writer ──────────────────────────────────────────────────────
    //
    // `form<TAB>freq` lines, freq as "0.####" invariant, sorted ordinally by
    // form. Ordinal sort + fixed format = a deterministic gzip artifact.
    private static void WriteLexicon(string outPath, Dictionary<string, double> map)
    {
        var forms = new List<string>(map.Keys);
        forms.Sort(StringComparer.Ordinal);

        using var file = File.Create(outPath);
        using var gz = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(gz, new UTF8Encoding(false));
        foreach (string form in forms)
            writer.Write($"{form}\t{map[form].ToString("0.####", CultureInfo.InvariantCulture)}\n");
    }

    private static void SelfCheck(string frenchOut, string englishOut)
    {
        Console.WriteLine();
        Console.WriteLine("Self-check (reload via FrequencyLexicon.LoadTsvGz):");

        var fr = FrequencyLexicon.LoadTsvGz(frenchOut);
        Console.WriteLine($"  French  count           {fr.Count,12:N0}");
        Console.WriteLine($"    freq(français)        {fr.FrequencyOf("français"),12:N4}");
        Console.WriteLine($"    freq(école)           {fr.FrequencyOf("école"),12:N4}");

        var en = FrequencyLexicon.LoadTsvGz(englishOut);
        Console.WriteLine($"  English count           {en.Count,12:N0}");
        Console.WriteLine($"    freq(the)             {en.FrequencyOf("the"),12:N4}");

        Console.WriteLine();
        Console.WriteLine("File sizes:");
        Console.WriteLine($"  {Path.GetFileName(frenchOut),-24}{new FileInfo(frenchOut).Length,12:N0} bytes");
        Console.WriteLine($"  {Path.GetFileName(englishOut),-24}{new FileInfo(englishOut).Length,12:N0} bytes");
    }

    // ── Filters / parsing ──────────────────────────────────────────────────

    // A form we keep: every char is a letter, an apostrophe or a hyphen. Drops
    // multi-word locutions (spaces) and junk; keeps « aujourd'hui », « peut-être ».
    private static bool IsAcceptedForm(string form)
    {
        if (form.Length == 0) return false;
        foreach (char c in form)
            if (!char.IsLetter(c) && c is not '\'' and not '’' and not '-')
                return false;
        return true;
    }

    private static bool IsPureAsciiLower(string word)
    {
        if (word.Length == 0) return false;
        foreach (char c in word)
            if (c is < 'a' or > 'z')
                return false;
        return true;
    }

    private static double ParseFreq(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;

    private static int IndexOf(string[] cols, string name)
    {
        int i = Array.IndexOf(cols, name);
        if (i < 0) throw new InvalidOperationException($"Lexique header has no '{name}' column.");
        return i;
    }
}
