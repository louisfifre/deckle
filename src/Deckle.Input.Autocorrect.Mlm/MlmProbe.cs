using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Deckle.Input.Autocorrect.Mlm;

// Offline probe (NOT the live engine) for the post-sentence reranker idea: can a
// CamemBERT masked-LM choose the right accented form among a CLOSED set, from
// bidirectional context? It mines BALANCED real cases for each ambiguous group
// (one bucket per surface form, so a model that always answers the common form
// is not flattered), masks the slot, and asks the model.
//
// Two readings are reported. Forced-choice accuracy = the raw signal "does it
// discriminate". The confidence curve = the product-relevant view: if we only
// act when the top-vs-second score margin clears a threshold, what precision do
// we buy and what fraction of cases do we still cover? The live reranker will
// sit somewhere on that curve.
public static class MlmProbe
{
    // Curated ambiguous groups: a folded key and its real French surface forms.
    // These are the high-frequency function-word ambiguities the left-context
    // n-gram could not resolve (a/à alone was the corpus's #1 miss). Content-word
    // pairs whose forms are multi-piece (cote/côté, tache/tâche, élève/élevé) need
    // per-subtoken PLL scoring; groups with any multi-piece form are skipped here
    // and reported, never silently dropped.
    private static readonly string[][] Groups =
    {
        new[] { "a", "à" },
        new[] { "ou", "où" },
        new[] { "la", "là" },
        new[] { "du", "dû" },
        new[] { "des", "dès" },
        new[] { "sur", "sûr" },
        new[] { "mur", "mûr" },
        new[] { "cote", "côte", "côté", "coté" },
    };

    private static readonly char[] SentenceBreaks = { '.', '!', '?', ';', ':', '…', '\n' };

    // Runs the probe. The caller supplies the resolved paths — the model
    // directory (holding model.onnx + the tokenizer files) and the evaluation
    // corpus — plus how many cases to mine per surface form and an optional TSV
    // dump of the per-case results. Returns 0 on success, 1 when an input is
    // missing.
    public static int Run(string modelDir, string corpus, int perForm = 120, string? outPath = null)
    {
        if (!File.Exists(Path.Combine(modelDir, "model.onnx")))
        {
            Console.Error.WriteLine($"Missing model: {Path.Combine(modelDir, "model.onnx")}");
            return 1;
        }
        if (!File.Exists(corpus))
        {
            Console.Error.WriteLine($"Missing corpus: {corpus}");
            return 1;
        }

        Console.WriteLine($"Model : {modelDir}");
        Console.WriteLine($"Corpus: {corpus}");
        Console.WriteLine($"Cases : up to {perForm} per surface form");
        Console.WriteLine();

        using var scorer = new CamembertMlmScorer(modelDir);

        // Resolve each group to its candidate ids; a group with any multi-piece
        // form is reported and skipped (it needs the PLL path, a follow-up).
        var live = new List<ProbeGroup>();
        foreach (string[] forms in Groups)
        {
            int[] ids = forms.Select(scorer.LeadingPieceId).ToArray();
            int bad = Array.IndexOf(ids, -1);
            if (bad >= 0)
            {
                Console.WriteLine($"  skip  [{string.Join("/", forms)}] — \"{forms[bad]}\" is not a single piece (needs PLL).");
                continue;
            }
            live.Add(new ProbeGroup(forms, ids));
        }
        Console.WriteLine();

        var cases = MineCases(corpus, live, perForm);

        int unknownTotal = 0;
        var results = new List<Result>(cases.Count);

        using var tsv = outPath is not null ? new StreamWriter(outPath, false, new UTF8Encoding(false)) : null;
        tsv?.WriteLine("group\tgold\tpred\tmargin\tcorrect\tsentence");

        foreach (var c in cases)
        {
            string prefix = c.Sentence[..c.SlotStart].TrimEnd();
            string suffix = c.Sentence[(c.SlotStart + c.SlotLen)..];

            int[] left = scorer.Encode(prefix, out int u1);
            int[] right = scorer.Encode(suffix, out int u2);
            unknownTotal += u1 + u2;

            float[] logits = scorer.MaskLogits(left, right);

            // Argmax over the group's candidate ids; margin = best minus runner-up.
            int bestK = 0; float best = float.NegativeInfinity, second = float.NegativeInfinity;
            for (int k = 0; k < c.Group.Ids.Length; k++)
            {
                float s = logits[c.Group.Ids[k]];
                if (s > best) { second = best; best = s; bestK = k; }
                else if (s > second) second = s;
            }
            string pred = c.Group.Forms[bestK];
            float margin = best - second;
            bool correct = pred == c.Gold;

            results.Add(new Result(c, pred, margin, correct));
            tsv?.WriteLine($"{c.Group.Forms[0]}\t{c.Gold}\t{pred}\t{margin:0.###}\t{(correct ? 1 : 0)}\t{c.Sentence}");
        }

        Report(live, results, unknownTotal);
        if (outPath is not null) Console.WriteLine($"\nPer-case results: {outPath}");
        return 0;
    }

    private static void Report(List<ProbeGroup> groups, List<Result> results, int unknownTotal)
    {
        int total = results.Count;
        int correct = results.Count(r => r.Correct);

        Console.WriteLine("── per group ──────────────────────────────────");
        foreach (var g in groups)
        {
            var gr = results.Where(r => ReferenceEquals(r.Case.Group, g)).ToList();
            if (gr.Count == 0) continue;
            int gc = gr.Count(r => r.Correct);
            Console.WriteLine($"  {string.Join("/", g.Forms),-22} {gc,5}/{gr.Count,-5} {Pct(gc, gr.Count),9}");
        }
        Console.WriteLine("────────────────────────────────────────────────");
        Console.WriteLine($"  forced-choice accuracy   {Pct(correct, total),9}   ({correct}/{total})");
        Console.WriteLine($"  unknown context pieces   {unknownTotal,9}");
        Console.WriteLine();

        // Confidence curve: act only when the margin clears a threshold. Coverage
        // = share of cases acted on; precision = accuracy among those. This is the
        // product tradeoff — the live reranker picks a point on it.
        Console.WriteLine("── confidence curve (act when margin ≥ T) ──────");
        Console.WriteLine("     T      coverage     precision");
        foreach (double t in new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 })
        {
            var acted = results.Where(r => r.Margin >= t).ToList();
            int actedCorrect = acted.Count(r => r.Correct);
            Console.WriteLine($"  {t,4:0.0}   {Pct(acted.Count, total),9}   {Pct(actedCorrect, acted.Count),11}");
        }

        var errors = results.Where(r => !r.Correct).OrderByDescending(r => r.Margin).ToList();
        if (errors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Highest-confidence errors (up to 12 of {errors.Count}):");
            foreach (var e in errors.Take(12))
                Console.WriteLine($"  [gold {e.Case.Gold} pred {e.Pred} m={e.Margin:0.##}]  {Trim(e.Case.Sentence)}");
        }
    }

    // Mines balanced cases: per group, one bucket per surface form, capped. Scans
    // the corpus in order, stops a group once every form bucket is full.
    private static List<SlotCase> MineCases(string corpusPath, List<ProbeGroup> groups, int perForm)
    {
        // group -> form -> cases
        var buckets = groups.ToDictionary(g => g, g => g.Forms.ToDictionary(f => f, _ => new List<SlotCase>()));
        var regexes = groups.ToDictionary(g => g, g => BuildRegex(g.Forms));

        foreach (string line in File.ReadLines(corpusPath))
        {
            foreach (string sentence in line.Split(SentenceBreaks, StringSplitOptions.RemoveEmptyEntries))
            {
                if (sentence.Trim().Length < 6) continue;
                foreach (var g in groups)
                {
                    var formBuckets = buckets[g];
                    foreach (Match m in regexes[g].Matches(sentence))
                    {
                        if (!formBuckets.TryGetValue(m.Value, out var bucket) || bucket.Count >= perForm)
                            continue;
                        if (sentence.Trim().Length <= m.Length) continue;
                        bucket.Add(new SlotCase(sentence, m.Index, m.Length, m.Value, g));
                    }
                }
            }
        }

        var all = new List<SlotCase>();
        foreach (var g in groups)
            foreach (var b in buckets[g].Values)
                all.AddRange(b);
        return all;
    }

    // A standalone occurrence of any of the forms: bounded by non-letters and not
    // glued to an apostrophe (elisions like "qu'a", "l'a" stay out). Longer/
    // accented forms first so alternation never matches a prefix.
    private static Regex BuildRegex(string[] forms)
    {
        string alt = string.Join("|", forms.OrderByDescending(f => f.Length).Select(Regex.Escape));
        return new Regex($@"(?<![\p{{L}}\p{{M}}'’])({alt})(?![\p{{L}}\p{{M}}'’])", RegexOptions.Compiled);
    }

    private static string Pct(int num, int den) =>
        den == 0 ? "N/A" : ((double)num / den).ToString("P2");

    private static string Trim(string s) => s.Length <= 90 ? s.Trim() : s.Trim()[..90] + "…";

    private sealed record ProbeGroup(string[] Forms, int[] Ids);
    private readonly record struct SlotCase(string Sentence, int SlotStart, int SlotLen, string Gold, ProbeGroup Group);
    private readonly record struct Result(SlotCase Case, string Pred, float Margin, bool Correct);
}
