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
    // the more generous of the two registers. Multi-word and junk forms are
    // dropped by the letter/apostrophe/hyphen filter.
    private static void BuildFrench(string sourcePath, string morphalouPath, string outPath, string verbsOutPath)
    {
        using var reader = new StreamReader(sourcePath, Encoding.UTF8);

        string? header = reader.ReadLine()
            ?? throw new InvalidOperationException($"Empty Lexique file: {sourcePath}");
        string[] cols = header.Split('\t');
        int iOrtho = IndexOf(cols, "ortho");
        int iFilms = IndexOf(cols, "freqfilms2");
        int iLivres = IndexOf(cols, "freqlivres");
        // Verb morphology: lemma (the infinitive), cgram (VER/AUX flags the verb
        // rows), infover (the mode:tense:person codes) and cgramortho (every
        // category the surface carries — the verb-only test). Resolved by name so
        // a column reorder upstream throws rather than silently misreading.
        int iLemme = IndexOf(cols, "lemme");
        int iCgram = IndexOf(cols, "cgram");
        int iInfover = IndexOf(cols, "infover");
        int iCgramOrtho = IndexOf(cols, "cgramortho");

        var sumFilms = new Dictionary<string, double>(StringComparer.Ordinal);
        var sumLivres = new Dictionary<string, double>(StringComparer.Ordinal);
        var verbs = new VerbAccumulator();

        long rows = 0, skippedForm = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            string[] f = line.Split('\t');
            if (f.Length <= iCgramOrtho) continue;

            string ortho = f[iOrtho];
            if (!IsAcceptedForm(ortho)) { skippedForm++; continue; }

            double films = ParseFreq(f[iFilms]);
            double livres = ParseFreq(f[iLivres]);

            sumFilms[ortho] = sumFilms.TryGetValue(ortho, out double pf) ? pf + films : films;
            sumLivres[ortho] = sumLivres.TryGetValue(ortho, out double pl) ? pl + livres : livres;
            rows++;

            // Capture the verb rows for the morphology artifact (VER conjugations
            // and AUX avoir/être). cgramortho holds every category the surface
            // carries, so the verb-only flag is read straight off it.
            if (f[iCgram] is "VER" or "AUX")
                verbs.Add(ortho, f[iLemme], f[iInfover], f[iCgramOrtho]);
        }

        // Final frequency per form = max of the two register sums.
        var final = new Dictionary<string, double>(sumFilms.Count, StringComparer.Ordinal);
        foreach (var (form, films) in sumFilms)
            final[form] = Math.Max(films, sumLivres.GetValueOrDefault(form, 0.0));

        int lexiqueCount = final.Count;
        int morphalouAdded = OverlayMorphalou(final, morphalouPath);

        WriteLexicon(outPath, final);
        int verbForms = verbs.Write(verbsOutPath);

        Console.WriteLine($"French: kept {lexiqueCount:N0} Lexique forms from {rows:N0} rows "
                        + $"({skippedForm:N0} filtered out).");
        Console.WriteLine(morphalouAdded >= 0
            ? $"        + {morphalouAdded:N0} Morphalou-only forms (epsilon freq) = {final.Count:N0} total."
            : "        Morphalou source not present — French lexicon is Lexique-only.");
        Console.WriteLine($"        verbs : {verbForms:N0} verb surface forms.");
    }

    // Overlays Morphalou inflected forms onto the Lexique map — but ONLY the ones
    // that bring unambiguous new coverage. A form is added at epsilon iff it is
    // the SOLE accented candidate of a fold Lexique leaves empty: then a bare
    // typing has one deterministic restoration the lexical gate fires on. Two
    // rejections matter, both measured: a form folding onto an EXISTING Lexique
    // fold would demote that fold's clean gate/dominance restoration into a
    // contested slot the conservative reranker abstains on (recall loss); and a
    // fold carrying TWO Morphalou forms (enlèves vs enlevés) is genuinely
    // ambiguous — adding both only feeds the reranker a slot it abstains on too.
    // Returns the count added, or -1 when no Morphalou source is present (the
    // step is optional, so `build-data` still runs Lexique-only without it).
    private static int OverlayMorphalou(Dictionary<string, double> french, string morphalouPath)
    {
        if (!File.Exists(morphalouPath))
            return -1;

        // Folds Lexique already populates — never crowd one of these.
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

    // Gathers the verb rows into the morphology artifact: one line per (surface
    // form, lemma), merging the codes of an ortho's AUX and VER rows (avoir shows
    // up as both), with a verb-only flag read off cgramortho. The flag is ANDed
    // across an ortho's rows so a single non-verb category makes the form
    // ambiguous for good — the conservative reading.
    private sealed class VerbAccumulator
    {
        // (ortho, lemma) → its merged infover codes, sorted for a deterministic line.
        private readonly Dictionary<(string Ortho, string Lemma), SortedSet<string>> _codes = new();
        // ortho → every category it carries is VER/AUX (no NOM/ADJ/… reading).
        private readonly Dictionary<string, bool> _verbOnly = new(StringComparer.Ordinal);

        public void Add(string ortho, string lemma, string infover, string cgramOrtho)
        {
            var key = (ortho, lemma);
            if (!_codes.TryGetValue(key, out SortedSet<string>? set))
                _codes[key] = set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string code in infover.Split(';', StringSplitOptions.RemoveEmptyEntries))
                set.Add(code);

            bool verbOnly = IsVerbOnly(cgramOrtho);
            _verbOnly[ortho] = _verbOnly.TryGetValue(ortho, out bool prior) ? prior && verbOnly : verbOnly;
        }

        // Every category in the comma-separated cgramortho is a verb category.
        private static bool IsVerbOnly(string cgramOrtho)
        {
            foreach (string c in cgramOrtho.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (c is not "VER" and not "AUX")
                    return false;
            return true;
        }

        // `form<TAB>lemma<TAB>codes<TAB>verbOnly`, codes ';'-joined, lines sorted
        // ordinally by (form, lemma) so the gzip artifact is byte-deterministic.
        // Returns the count of distinct verb surface forms.
        public int Write(string outPath)
        {
            var keys = new List<(string Ortho, string Lemma)>(_codes.Keys);
            keys.Sort(static (a, b) =>
            {
                int byOrtho = string.CompareOrdinal(a.Ortho, b.Ortho);
                return byOrtho != 0 ? byOrtho : string.CompareOrdinal(a.Lemma, b.Lemma);
            });

            using var file = File.Create(outPath);
            using var gz = new GZipStream(file, CompressionLevel.Optimal);
            using var writer = new StreamWriter(gz, new UTF8Encoding(false));

            var distinctForms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (ortho, lemma) in keys)
            {
                distinctForms.Add(ortho);
                string codes = string.Join(';', _codes[(ortho, lemma)]);
                string verbOnly = _verbOnly.GetValueOrDefault(ortho, false) ? "1" : "0";
                writer.Write($"{ortho}\t{lemma}\t{codes}\t{verbOnly}\n");
            }
            return distinctForms.Count;
        }
    }
}
