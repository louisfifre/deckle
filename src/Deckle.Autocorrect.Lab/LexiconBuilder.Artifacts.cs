using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

public static partial class LexiconBuilder
{
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

    private static void SelfCheck(string frenchOut, string englishOut, string verbsOut, string globishOut)
    {
        Console.WriteLine();
        Console.WriteLine("Self-check (reload via FrequencyLexicon.LoadTsvGz):");

        var fr = FrequencyLexicon.LoadTsvGz(frenchOut);
        Console.WriteLine($"  French  count           {fr.Count,12:N0}");
        Console.WriteLine($"    freq(français)        {fr.FrequencyOf("français"),12:N4}");
        Console.WriteLine($"    freq(école)           {fr.FrequencyOf("école"),12:N4}");
        // Conjugations Lexique omits but Morphalou carries — non-zero confirms
        // the overlay landed (they sit at the epsilon frequency).
        Console.WriteLine($"    freq(captes)          {fr.FrequencyOf("captes"),12:N4}");
        Console.WriteLine($"    freq(renommes)        {fr.FrequencyOf("renommes"),12:N4}");

        var en = FrequencyLexicon.LoadTsvGz(englishOut);
        Console.WriteLine($"  English count           {en.Count,12:N0}");
        Console.WriteLine($"    freq(the)             {en.FrequencyOf("the"),12:N4}");

        if (File.Exists(globishOut))
        {
            var globish = FrequencyLexicon.LoadTsvGz(globishOut);
            Console.WriteLine($"  Globish count           {globish.Count,12:N0}");
            Console.WriteLine($"    freq(greenwashing)    {globish.FrequencyOf("greenwashing"),12:N4}");
            Console.WriteLine($"    freq(the)             {globish.FrequencyOf("the"),12:N4}");
        }
        else
        {
            Console.WriteLine("  Globish count                    absent");
        }

        var vb = VerbMorphology.LoadTsvGz(verbsOut);

        Console.WriteLine($"  Verb    forms           {vb.Count,12:N0}");
        // manges → manger ind:pre:2s; the 1s slot of the same lemma is "mange".
        Console.WriteLine($"    manges is verb        {vb.IsVerb("manges"),12}");
        Console.WriteLine($"    manger ind:pre:1s     {vb.Conjugate("manger", "ind", "pre", "1s"),12}");
        // "ferme" is fermer-the-verb but also a noun — ambiguous, never agreed.
        Console.WriteLine($"    ferme ambiguous       {vb.IsAmbiguous("ferme"),12}");

        Console.WriteLine();
        Console.WriteLine("File sizes:");
        Console.WriteLine($"  {Path.GetFileName(frenchOut),-24}{new FileInfo(frenchOut).Length,12:N0} bytes");
        Console.WriteLine($"  {Path.GetFileName(englishOut),-24}{new FileInfo(englishOut).Length,12:N0} bytes");
        if (File.Exists(globishOut))
            Console.WriteLine($"  {Path.GetFileName(globishOut),-24}{new FileInfo(globishOut).Length,12:N0} bytes");
        Console.WriteLine($"  {Path.GetFileName(verbsOut),-24}{new FileInfo(verbsOut).Length,12:N0} bytes");
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
