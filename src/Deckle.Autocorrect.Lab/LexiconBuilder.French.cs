using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

public static partial class LexiconBuilder
{
    // ── French: Lexique 3.83 ───────────────────────────────────────────────
    //
    // The header names the columns; we resolve ortho/freqfilms2/freqlivres by
    // name so a column reorder upstream cannot silently misread. Per ortho we
    // sum the film and book frequencies separately across its rows (the same
    // surface appears once per lemma/POS), then take the max of the two sums —
    // the more generous of the two registers.
    private static void BuildFrench(string sourcePath, string morphalouPath, string outPath)
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
            if (f.Length <= Math.Max(iOrtho, Math.Max(iFilms, iLivres))) continue;

            string ortho = f[iOrtho];
            if (!IsAcceptedForm(ortho)) { skippedForm++; continue; }

            double films = ParseFreq(f[iFilms]);
            double livres = ParseFreq(f[iLivres]);

            sumFilms[ortho] = sumFilms.TryGetValue(ortho, out double pf) ? pf + films : films;
            sumLivres[ortho] = sumLivres.TryGetValue(ortho, out double pl) ? pl + livres : livres;
            rows++;
        }

        var final = new Dictionary<string, double>(sumFilms.Count, StringComparer.Ordinal);
        foreach (var (form, films) in sumFilms)
            final[form] = Math.Max(films, sumLivres.GetValueOrDefault(form, 0.0));

        int lexiqueCount = final.Count;
        int morphalouAdded = OverlayMorphalou(final, morphalouPath);

        WriteLexicon(outPath, final);

        Console.WriteLine($"French: kept {lexiqueCount:N0} Lexique forms from {rows:N0} rows "
                        + $"({skippedForm:N0} filtered out).");
        Console.WriteLine(morphalouAdded >= 0
            ? $"        + {morphalouAdded:N0} Morphalou-only forms (epsilon freq) = {final.Count:N0} total."
            : "        Morphalou source not present — French lexicon is Lexique-only.");
    }

    // Overlays Morphalou inflected forms onto the Lexique map, but ONLY the ones
    // that bring unambiguous new coverage. A form is added at epsilon iff it is
    // the SOLE accented candidate of a fold Lexique leaves empty: then a bare
    // typing has one deterministic restoration the lexical gate fires on. A form
    // folding onto an EXISTING Lexique fold would demote that fold's clean
    // gate/dominance restoration into a contested slot the conservative gate
    // abstains on; a fold carrying TWO Morphalou forms (enlèves vs enlevés) is
    // genuinely ambiguous, and adding both only makes a slot it abstains on too. Returns the count added, or -1 when no Morphalou source
    // is present.
    private static int OverlayMorphalou(Dictionary<string, double> french, string morphalouPath)
    {
        if (!File.Exists(morphalouPath))
            return -1;

        var lexiqueFolds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string form in french.Keys)
            lexiqueFolds.Add(AccentFolding.Fold(form));

        // fold → its sole candidate form, or null once a second distinct form
        // proves the fold ambiguous.
        var byFold = new Dictionary<string, string?>(StringComparer.Ordinal);
        using (var reader = new StreamReader(morphalouPath, Encoding.UTF8))
        {
            foreach (string graphie in MorphalouReader.ReadInflectedForms(reader))
            {
                string form = graphie.ToLowerInvariant();
                if (!IsAcceptedForm(form) || french.ContainsKey(form))
                    continue;
                string fold = AccentFolding.Fold(form);
                if (lexiqueFolds.Contains(fold))
                    continue;

                if (byFold.TryGetValue(fold, out string? sole))
                {
                    if (sole is not null && sole != form)
                        byFold[fold] = null; // a second distinct form — ambiguous
                }
                else
                {
                    byFold[fold] = form;
                }
            }
        }

        int added = 0;
        foreach (var (_, form) in byFold)
        {
            if (form is null)
                continue;
            french[form] = MorphalouEpsilonPerMillion;
            added++;
        }
        return added;
    }
}
