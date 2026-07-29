using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// Fabricates a domain pack — an activatable set of surface forms that fully
// extends the primary lexicon (valid forms AND correction targets) — from a
// kaikki.org per-language wiktionary raw extraction. Conflicts with the base
// lexicon are resolved here, at fabrication, never at runtime: a candidate
// whose masking cost (base-lexicon frequency mass within edit distance 1)
// exceeds the exclusion threshold is refused, and the gray zone below it stays
// withheld until an external LLM judge records a verdict in the pack's
// judgments file.
// The shipped pack is already clean; the fabrication report carries the
// dilution indicator (what the pack brings, what was refused) and journals
// the judge's verdicts.
public static class DomainPackBuilder
{
    // Frequency stamped on a shipped pack form the promotion overlay does not
    // carry. Out-of-Lexique sources have no frequency of their own, so forms
    // start at a flat floor sitting well below the base lexicon's 1 opm tail.
    // The IT bench showed the floor alone breaks the pack's own contract: a
    // pack form at 0.2 loses the correction contest to any nearby base
    // candidate (« hebergeur » was still corrected to « héberger »), so
    // genuinely common terms are promoted at build through the versioned
    // wordfreq overlay next to the judgments file — shipped frequency is
    // max(floor, overlay).
    public const double FloorFrequencyPerMillion = 0.2;

    // Sanitization thresholds, in base-lexicon opm mass within edit distance 1.
    // Provisional values pending the pilot-pack bench: above Exclusion the form
    // is refused outright; between GrayZone and Exclusion it is withheld until
    // judged; below GrayZone it ships.
    private const double MaskingExclusionPerMillion = 20.0;
    private const double MaskingGrayZonePerMillion = 1.0;

    // The pilot pack: French computing vocabulary. Category names are the exact
    // frwiktionary sense/entry categories (typographic apostrophe U+2019, as
    // the dump spells them); a name matching nothing is inert, so the list can
    // widen freely between rebuilds.
    public static DomainPackDefinition FrItPack { get; } = new(
        "fr", "it",
        [
            "Lexique en français de l’informatique",
            "Lexique en français de l’Internet",
            "Lexique en français de la programmation",
        ]);

    // The same domain in English, mined from the enwiktionary extraction. Under
    // a French primary lexicon this pack is protection-only in practice — it
    // carries the everyday franglais the French Wiktionary categories never
    // held (backend, plugin, firmware) — but it is fabricated by the same chain
    // and sanitized against the same French base, so masking cost still gates
    // every form. Categories are the exact enwiktionary names, prefix included.
    public static DomainPackDefinition EnItPack { get; } = new(
        "en", "it",
        [
            "en:Computing",
            "en:Internet",
            "en:Software",
            "en:Programming",
        ]);

    // Builds one pack: streams the raw dump, harvests the surface forms of the
    // matching entries (lemma + inline inflections), sanitizes them against the
    // base lexicon, applies the journaled judge verdicts, then writes the gzip
    // TSV artifact into outDir and the fabrication report + judge worksheet
    // into reportDir. Deterministic over an unchanged dump + judgments file.
    // Returns 0 on success.
    public static int Run(
        string dumpPath, string baseLexiconPath, string outDir, string reportDir,
        DomainPackDefinition pack)
    {
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(reportDir);

        var baseLexicon = FrequencyLexicon.LoadTsvGz(baseLexiconPath);
        var judgments = LoadJudgments(Path.Combine(reportDir, pack.JudgmentsFileName));
        var promotions = LoadPromotions(Path.Combine(reportDir, pack.FrequenciesFileName));

        Console.WriteLine($"Pack {pack.Id}: harvesting {Path.GetFileName(dumpPath)} ...");
        var harvest = HarvestForms(dumpPath, pack, baseLexicon);
        Console.WriteLine($"Pack {pack.Id}: {harvest.EntriesMatched:N0} entries matched, "
                        + $"{harvest.Candidates.Count:N0} candidate forms "
                        + $"({harvest.AlreadyInBase:N0} already in base, "
                        + $"{harvest.ShapeRejected:N0} shape rejects, "
                        + $"{harvest.ArtifactRejected:N0} table artifacts).");

        var alphabet = BuildAlphabet(baseLexicon);
        var shipped = new Dictionary<string, double>(StringComparer.Ordinal);
        var refused = new List<(string Form, double Cost)>();
        var gray = new List<(string Form, double Cost, string Verdict, string Note)>();

        foreach (string form in harvest.Candidates)
        {
            double cost = MaskingCost(form, baseLexicon, alphabet);
            if (cost >= MaskingExclusionPerMillion)
            {
                refused.Add((form, cost));
                continue;
            }
            if (cost >= MaskingGrayZonePerMillion)
            {
                if (judgments.TryGetValue(form, out var judged))
                {
                    gray.Add((form, cost, judged.Admit ? "admit" : "exclude", judged.Note));
                    if (judged.Admit)
                        shipped[form] = FloorFrequencyPerMillion;
                }
                else
                {
                    gray.Add((form, cost, "pending", ""));
                }
                continue;
            }
            shipped[form] = FloorFrequencyPerMillion;
        }

        int promoted = 0;
        foreach (string form in shipped.Keys.ToList())
        {
            if (promotions.TryGetValue(form, out double opm) && opm > shipped[form])
            {
                shipped[form] = opm;
                promoted++;
            }
        }

        string packPath = Path.Combine(outDir, pack.FileName);
        LexiconBuilder.WriteLexicon(packPath, shipped);
        WriteReport(
            Path.Combine(reportDir, pack.ReportFileName),
            pack, harvest, shipped.Count, promoted, refused, gray);

        int pending = gray.Count(g => g.Verdict == "pending");

        // The machine-readable half of the dilution indicator, shipped in
        // outDir beside the forms it describes so the settings page can state
        // what the pack brings and what was refused without parsing the report.
        // Same counts as the report's Yield table, written in the same pass.
        new DomainPackManifest
        {
            Id = pack.Id,
            ShippedForms = shipped.Count,
            PromotedForms = promoted,
            RefusedAboveThreshold = refused.Count,
            RefusedByJudge = gray.Count(g => g.Verdict == "exclude"),
            PendingJudgment = pending,
            AlreadyInBaseLexicon = (int)harvest.AlreadyInBase,
        }.Write(Path.Combine(outDir, pack.ManifestFileName));
        Console.WriteLine($"Pack {pack.Id}: shipped {shipped.Count:N0} forms "
                        + $"({promoted:N0} frequency-promoted), "
                        + $"refused {refused.Count:N0} above threshold, "
                        + $"gray zone {gray.Count:N0} ({pending:N0} pending judgment).");
        Console.WriteLine($"  {Path.GetFileName(packPath),-24}{new FileInfo(packPath).Length,12:N0} bytes");
        return 0;
    }

    // Merges the base lexicon and a pack into one effective-lexicon TSV, the
    // exact table the runtime will consult: on a duplicated form the highest
    // frequency wins — commutative and idempotent, so activation order can
    // never matter. Lab-side only for now; the runtime keeps its existing
    // mechanics until the pilot pack has passed the bench.
    public static void MergeEffective(string baseLexiconPath, string packPath, string outPath)
    {
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (form, freq) in FrequencyLexicon.LoadTsvGz(baseLexiconPath).Entries)
            merged[form] = freq;
        foreach (var (form, freq) in FrequencyLexicon.LoadTsvGz(packPath).Entries)
            merged[form] = Math.Max(freq, merged.TryGetValue(form, out double prior) ? prior : 0.0);
        LexiconBuilder.WriteLexicon(outPath, merged);
    }

    // ── Harvest ────────────────────────────────────────────────────────────

    private sealed record HarvestResult(
        SortedSet<string> Candidates,
        long EntriesMatched,
        long AlreadyInBase,
        long ShapeRejected,
        long ArtifactRejected);

    // Streams the gzip JSONL dump line by line. A cheap substring prefilter
    // keeps JSON parsing off the millions of non-matching lines; the parsed
    // check then requires the pack's language and an exact category match —
    // categories live at sense level (entry level scanned too). Harvested forms
    // are the entry word plus its inline inflections, minus the wiktextract
    // table artifacts.
    private static HarvestResult HarvestForms(
        string dumpPath, DomainPackDefinition pack, FrequencyLexicon baseLexicon)
    {
        var candidates = new SortedSet<string>(StringComparer.Ordinal);
        long matched = 0, alreadyInBase = 0, shapeRejected = 0, artifactRejected = 0;

        using var file = File.OpenRead(dumpPath);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!Prefilter(line, pack))
                continue;

            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (!MatchesPack(root, pack))
                continue;

            matched++;
            foreach (JsonElement candidate in SurfaceForms(root))
            {
                if (IsTableArtifact(candidate))
                {
                    artifactRejected++;
                    continue;
                }
                if (!TryNormalizeForm(FormText(candidate), out string form))
                {
                    shapeRejected++;
                    continue;
                }
                if (baseLexicon.Contains(form))
                {
                    alreadyInBase++;
                    continue;
                }
                candidates.Add(form);
            }
        }

        return new HarvestResult(
            candidates, matched, alreadyInBase, shapeRejected, artifactRejected);
    }

    // The cheap gate that keeps JsonDocument.Parse off the lines that cannot
    // match: a line carrying none of the pack's category names carries none of
    // its entries. Derived from the perimeter rather than anchored on a literal
    // prefix, so widening the category list can never leave the prefilter
    // behind. Assumes the dump spells category names as literal UTF-8, which is
    // what the parsed exact match already relies on.
    private static bool Prefilter(string line, DomainPackDefinition pack)
    {
        foreach (string category in pack.Categories)
            if (line.Contains(category, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool MatchesPack(JsonElement root, DomainPackDefinition pack)
    {
        if (!root.TryGetProperty("lang_code", out JsonElement lang)
            || lang.ValueKind != JsonValueKind.String
            || lang.GetString() != pack.Language)
            return false;

        if (HasPackCategory(root, pack))
            return true;

        if (root.TryGetProperty("senses", out JsonElement senses)
            && senses.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement sense in senses.EnumerateArray())
                if (HasPackCategory(sense, pack))
                    return true;
        }
        return false;
    }

    // Category arrays hold plain strings in the raw dump; the per-word exports
    // wrap them in {"name": ...} objects — both shapes are accepted.
    private static bool HasPackCategory(JsonElement element, DomainPackDefinition pack)
    {
        if (!element.TryGetProperty("categories", out JsonElement categories)
            || categories.ValueKind != JsonValueKind.Array)
            return false;

        foreach (JsonElement category in categories.EnumerateArray())
        {
            string? name = category.ValueKind switch
            {
                JsonValueKind.String => category.GetString(),
                JsonValueKind.Object when category.TryGetProperty("name", out JsonElement n)
                    && n.ValueKind == JsonValueKind.String => n.GetString(),
                _ => null,
            };
            if (name is not null && pack.Categories.Contains(name))
                return true;
        }
        return false;
    }

    // The entry's surface candidates, each yielded as the JSON element that
    // carries it: the lemma as a bare string, every inflection as its full
    // forms[] object — the object is what the artifact filter needs to read.
    private static IEnumerable<JsonElement> SurfaceForms(JsonElement root)
    {
        if (root.TryGetProperty("word", out JsonElement word)
            && word.ValueKind == JsonValueKind.String)
            yield return word;

        if (!root.TryGetProperty("forms", out JsonElement forms)
            || forms.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (JsonElement entry in forms.EnumerateArray())
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("form", out JsonElement form)
                && form.ValueKind == JsonValueKind.String)
                yield return entry;
    }

    private static string FormText(JsonElement candidate) =>
        (candidate.ValueKind == JsonValueKind.String
            ? candidate.GetString()
            : candidate.GetProperty("form").GetString())!;

    // wiktextract emits inflection-table scaffolding as ordinary forms[]
    // entries — « no-table-tags » is one, and its masking cost is zero, so
    // nothing downstream would stop it shipping. They are recognized by the
    // « table-tags » marker they carry, never by their text: a value blacklist
    // would one day refuse a legitimate word that happened to collide.
    private static bool IsTableArtifact(JsonElement candidate)
    {
        if (candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("tags", out JsonElement tags)
            || tags.ValueKind != JsonValueKind.Array)
            return false;

        foreach (JsonElement tag in tags.EnumerateArray())
            if (tag.ValueKind == JsonValueKind.String
                && tag.ValueEquals("table-tags"))
                return true;
        return false;
    }

    // Same normalization contract as the base lexicon store (lowercase + NFC,
    // ASCII apostrophe) so lookups by lowercased literal always agree. Shape
    // mirrors IsAcceptedForm: letters, apostrophe, hyphen — locutions with
    // spaces fall out here, the commit stage corrects single words only.
    private static bool TryNormalizeForm(string raw, out string form)
    {
        form = raw.Trim()
            .Replace('’', '\'')
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormC);
        if (form.Length < 2 || form[0] == '-' || form[^1] == '-')
            return false;
        foreach (char c in form)
            if (!char.IsLetter(c) && c is not '\'' and not '-')
                return false;
        return true;
    }

    // ── Sanitization ───────────────────────────────────────────────────────

    // The characters a distance-1 variant may introduce: exactly the alphabet
    // the base lexicon is written in, derived rather than hardcoded so the
    // neighbourhood always matches the actual data.
    private static char[] BuildAlphabet(FrequencyLexicon baseLexicon)
    {
        var chars = new SortedSet<char>();
        foreach (var (form, _) in baseLexicon.Entries)
            foreach (char c in form)
                chars.Add(c);
        return [.. chars];
    }

    // Masking cost of a candidate form: the total base-lexicon frequency mass
    // (opm) within Damerau-Levenshtein distance 1 — every base word a typo of
    // which could be captured or shielded by the new form. Computed by
    // enumerating the candidate's distance-1 neighbourhood against the base
    // map: deletions, transpositions, substitutions and insertions.
    private static double MaskingCost(string form, FrequencyLexicon baseLexicon, char[] alphabet)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { form };
        double mass = 0.0;

        void Probe(string variant)
        {
            if (seen.Add(variant))
                mass += baseLexicon.FrequencyOf(variant);
        }

        for (int i = 0; i < form.Length; i++)
            Probe(form.Remove(i, 1));

        for (int i = 0; i < form.Length - 1; i++)
        {
            char[] swapped = form.ToCharArray();
            (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
            Probe(new string(swapped));
        }

        foreach (char c in alphabet)
        {
            for (int i = 0; i < form.Length; i++)
            {
                if (form[i] != c)
                    Probe(string.Concat(form.AsSpan(0, i), c.ToString(), form.AsSpan(i + 1)));
                Probe(form.Insert(i, c.ToString()));
            }
            Probe(form + c);
        }

        return mass;
    }

    // ── Judge verdicts ─────────────────────────────────────────────────────

    // The judgments file is the machine-readable side of the external LLM
    // arbitration: `form<TAB>admit|exclude<TAB>note` lines, versioned next to
    // the report. The builder never writes it — verdicts are recorded by the
    // maintainer's judge campaign and applied on the next build.
    private static Dictionary<string, (bool Admit, string Note)> LoadJudgments(string path)
    {
        var verdicts = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return verdicts;

        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] cols = line.Split('\t');
            if (cols.Length < 2 || cols[1] is not ("admit" or "exclude"))
                throw new InvalidDataException($"Malformed judgment line: {line}");
            verdicts[cols[0]] = (cols[1] == "admit", cols.Length > 2 ? cols[2] : "");
        }
        return verdicts;
    }

    // The promotion overlay is the machine-readable outcome of the bench's
    // floor-vs-promotion call: `form<TAB>opm` lines (wordfreq-derived, simple
    // single-token forms only), versioned next to the judgments file. Curated
    // by the maintainer's promotion gesture, never written by the builder;
    // a shipped form absent from the overlay stays at the floor.
    private static Dictionary<string, double> LoadPromotions(string path)
    {
        var promotions = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return promotions;

        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] cols = line.Split('\t');
            if (cols.Length != 2
                || !double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double opm)
                || opm <= 0.0)
                throw new InvalidDataException($"Malformed promotion line: {line}");
            promotions[cols[0]] = opm;
        }
        return promotions;
    }

    // ── Report ─────────────────────────────────────────────────────────────

    // The fabrication report: yield of every stage, the dilution indicator the
    // pack UI will surface, the full refusal list, and the gray-zone worksheet
    // the judge campaign reads and whose verdicts it journals. All lists are
    // complete and sorted — no silent caps, byte-deterministic.
    private static void WriteReport(
        string path, DomainPackDefinition pack, HarvestResult harvest,
        int shippedCount, int promotedCount,
        List<(string Form, double Cost)> refused,
        List<(string Form, double Cost, string Verdict, string Note)> gray)
    {
        var sb = new StringBuilder();
        string Opm(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        int admitted = gray.Count(g => g.Verdict == "admit");
        int excluded = gray.Count(g => g.Verdict == "exclude");
        int pending = gray.Count(g => g.Verdict == "pending");

        sb.Append($"# Pack {pack.Id} — fabrication report\n\n");
        sb.Append($"Source: kaikki.org {pack.Language}wiktionary raw extraction (see NOTICE.md). ");
        sb.Append("Regenerated by the pack's maintenance gesture; deterministic ");
        sb.Append("over an unchanged dump and judgments file.\n\n");

        sb.Append("Categories:\n");
        foreach (string category in pack.Categories)
            sb.Append($"- {category}\n");
        sb.Append('\n');

        sb.Append("## Yield\n\n");
        sb.Append("| stage | count |\n|---|---|\n");
        sb.Append($"| entries matched | {harvest.EntriesMatched} |\n");
        sb.Append($"| forms rejected as table artifacts | {harvest.ArtifactRejected} |\n");
        sb.Append($"| forms rejected by shape | {harvest.ShapeRejected} |\n");
        sb.Append($"| forms already in base lexicon | {harvest.AlreadyInBase} |\n");
        sb.Append($"| candidate forms sanitized | {harvest.Candidates.Count} |\n");
        sb.Append($"| refused (masking ≥ {Opm(MaskingExclusionPerMillion)} opm) | {refused.Count} |\n");
        sb.Append($"| gray zone ({Opm(MaskingGrayZonePerMillion)}–{Opm(MaskingExclusionPerMillion)} opm) "
                + $"| {gray.Count} (admit {admitted}, exclude {excluded}, pending {pending}) |\n");
        sb.Append($"| shipped | {shippedCount} ({promotedCount} frequency-promoted, "
                + $"rest at floor {Opm(FloorFrequencyPerMillion)} opm) |\n\n");

        sb.Append("## Dilution indicator\n\n");
        sb.Append($"The pack brings {shippedCount} correctable forms; ");
        sb.Append($"{refused.Count + excluded} were refused to protect base corrections ");
        sb.Append($"({refused.Count} above the masking threshold, {excluded} judged out) ");
        sb.Append($"and {pending} stay withheld pending judgment.\n\n");
        sb.Append($"The same counts ship as `{pack.ManifestFileName}` beside the pack artifact — ");
        sb.Append("the machine-readable side the settings page reads.\n\n");

        sb.Append("## Refused above threshold\n\n");
        sb.Append("| form | masking cost (opm) |\n|---|---|\n");
        foreach (var (form, cost) in refused.OrderByDescending(r => r.Cost).ThenBy(r => r.Form, StringComparer.Ordinal))
            sb.Append($"| {form} | {Opm(cost)} |\n");
        sb.Append('\n');

        sb.Append("## Gray zone — judge worksheet\n\n");
        sb.Append($"Verdicts live in `{pack.JudgmentsFileName}` (`form<TAB>admit|exclude<TAB>note`); ");
        sb.Append("a pending form is withheld from the pack until judged.\n\n");
        sb.Append("| form | masking cost (opm) | verdict | note |\n|---|---|---|---|\n");
        foreach (var (form, cost, verdict, note) in
                 gray.OrderByDescending(g => g.Cost).ThenBy(g => g.Form, StringComparer.Ordinal))
            sb.Append($"| {form} | {Opm(cost)} | {verdict} | {note} |\n");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}

// One buildable domain pack: the language of the entries it mines, its domain
// key, and the wiktionary categories that define its perimeter. Every file name
// derives from Id — the same string the runtime's DomainPack carries — so the
// artifacts, their report and the pack the app loads agree by construction
// rather than by two interpolations happening to match.
public sealed record DomainPackDefinition(
    string Language, string Key, IReadOnlyList<string> Categories)
{
    public string Id => $"{Language}-{Key}";

    public string FileName => $"pack-{Id}.tsv.gz";
    public string ManifestFileName => $"pack-{Id}.manifest.json";
    public string ReportFileName => $"pack-{Id}.md";
    public string JudgmentsFileName => $"pack-{Id}.judgments.tsv";
    public string FrequenciesFileName => $"pack-{Id}.frequencies.tsv";
}
