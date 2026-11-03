using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Deckle.Benchmark.ModelCompare;

// ─── Summary: one table per run, then every finding for a human to read ──────
//
// Two kinds of numbers side by side. The judge's counts say what an LLM saw;
// the marker deltas are counted in code, with no API, so they can be trusted
// to be the same on every rerun and catch what the judge may miss (a hedge
// that disappeared, a negation that appeared). Neither is truth: the
// per-generation section is there so a person reads the actual spans.

static partial class Summary
{
    public static int Run(string runDir)
    {
        string genPath = Path.Combine(runDir, "generations.jsonl");
        string judgePath = Path.Combine(runDir, "judgements.jsonl");
        var generations = Json.ReadLines<GenerationRow>(genPath).Where(g => g.Error is null && g.Text is not null).ToList();
        var judgements = File.Exists(judgePath) ? Json.ReadLines<JudgementRow>(judgePath) : [];
        var raw = Corpus.LoadRaw();

        var byKey = judgements
            .GroupBy(j => (j.Id, j.Model, j.Prompt, j.Source))
            .ToDictionary(g => g.Key, g => g.Last()); // a rerun judge appends; the last verdict wins

        var md = new StringBuilder();
        md.AppendLine($"# Rewrite model comparison — {Path.GetFileName(Path.GetFullPath(runDir).TrimEnd('\\'))}");
        md.AppendLine();
        string judgeModels = string.Join(", ", judgements.Select(j => j.JudgeModel).Distinct());
        md.AppendLine($"Generations: {generations.Count}. Judgements: {judgements.Count}{(judgeModels.Length > 0 ? $" (judge: {judgeModels})" : "")}.");
        md.AppendLine();

        md.AppendLine("| model | prompt | source | n | words out/in | chars out/in | terms | inversions | hedges | omissions | additions | faithful/minor/unfaithful | neg Δ/100w | neg Δ excl. ne | hedge Δ/100w | avg ms | gen $ | judge $ |");
        md.AppendLine("|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|---|--:|--:|--:|--:|--:|--:|");

        var groups = generations
            .GroupBy(g => (g.Model, g.Prompt, g.Source))
            .OrderBy(g => g.Key.Source).ThenBy(g => g.Key.Model).ThenBy(g => g.Key.Prompt);

        foreach (var group in groups)
        {
            var rows = group.ToList();
            var judged = rows
                .Select(g => byKey.TryGetValue((g.Id, g.Model, g.Prompt, g.Source), out var j) ? j : null)
                .Where(j => j is not null).Select(j => j!).ToList();
            var parsed = judged.Where(j => j.Judgement is not null).Select(j => j.Judgement!).ToList();

            int Sum(Func<Judgement, List<Finding>?> pick) => parsed.Sum(j => pick(j)?.Count ?? 0);
            int Verdicts(string v) => parsed.Count(j => string.Equals(j.Verdict, v, StringComparison.OrdinalIgnoreCase));

            var markers = rows.Where(g => raw.ContainsKey(g.Id)).Select(g => Markers.Delta(raw[g.Id].Text, g.Text!)).ToList();
            double Avg(Func<MarkerDelta, double> pick) => markers.Count == 0 ? 0 : markers.Average(pick);

            double? genCost = rows.All(g => g.CostUsd is null) ? null : rows.Sum(g => g.CostUsd ?? 0);
            double judgeCost = judged.Sum(j => j.JudgeCostUsd ?? 0);
            var timed = rows.Where(g => g.TotalMs is not null).ToList();

            md.AppendLine(string.Join(" | ", [
                $"| {group.Key.Model}", group.Key.Prompt, group.Key.Source, rows.Count.ToString(CultureInfo.InvariantCulture),
                F(Avg(m => m.WordRatio)), F(Avg(m => m.CharRatio)),
                Sum(j => j.TermSubstitutions).ToString(CultureInfo.InvariantCulture),
                Sum(j => j.MeaningInversions).ToString(CultureInfo.InvariantCulture),
                Sum(j => j.HedgesDropped).ToString(CultureInfo.InvariantCulture),
                Sum(j => j.Omissions).ToString(CultureInfo.InvariantCulture),
                Sum(j => j.Additions).ToString(CultureInfo.InvariantCulture),
                $"{Verdicts("faithful")}/{Verdicts("minor")}/{Verdicts("unfaithful")}" + (judged.Count < rows.Count ? $" ({rows.Count - judged.Count} unjudged)" : "")
                    + (parsed.Count < judged.Count ? $" ({judged.Count - parsed.Count} unparsed)" : ""),
                Signed(Avg(m => m.NegationPer100)), Signed(Avg(m => m.NegationNoNePer100)), Signed(Avg(m => m.HedgePer100)),
                timed.Count == 0 ? "" : timed.Average(g => g.TotalMs!.Value).ToString("F0", CultureInfo.InvariantCulture),
                genCost is null ? "—" : genCost.Value.ToString("F4", CultureInfo.InvariantCulture),
                judgeCost.ToString("F4", CultureInfo.InvariantCulture) + " |"]));
        }

        md.AppendLine();
        md.AppendLine("Ratios are means over the group's generations. Marker deltas are rewrite minus raw, in occurrences per 100 words, averaged:");
        md.AppendLine("negation markers are ne, n', pas, jamais, aucun(e), plus, moins (restoring a dropped \"ne\" is normal cleaning, so the column without ne/n' is the one to watch);");
        md.AppendLine("hedge markers are je pense, je crois, peut-être, je (ne) sais pas, il me semble, j'ai l'impression. Costs come from the pricing snapshot in Shared.cs; a production rewrite has no recorded tokens.");
        md.AppendLine();

        md.AppendLine("## Findings by generation");
        foreach (var group in groups)
        {
            md.AppendLine();
            md.AppendLine($"### {group.Key.Model} · {group.Key.Prompt} · {group.Key.Source}");
            foreach (var gen in group.OrderBy(g => g.Bucket).ThenBy(g => g.Id))
            {
                md.AppendLine();
                var m = raw.TryGetValue(gen.Id, out var r) ? Markers.Delta(r.Text, gen.Text!) : null;
                string counts = m is null ? "" :
                    $" — words {m.RawWords}→{m.RewriteWords}, negations (excl. ne) {m.RawNegationsNoNe}→{m.RewriteNegationsNoNe}, hedges {m.RawHedges}→{m.RewriteHedges}";
                byKey.TryGetValue((gen.Id, gen.Model, gen.Prompt, gen.Source), out var j);
                string verdict = j is null ? "not judged" : j.Error ?? j.Judgement?.Verdict ?? "judge answer did not parse";
                md.AppendLine($"**{gen.Id}** ({gen.Bucket}) — {verdict}{counts}");
                if (j?.Judgement is null) continue;
                md.AppendLine();
                bool any = false;
                foreach (var (category, items) in j.Judgement.Categories())
                {
                    foreach (var item in items)
                    {
                        any = true;
                        string raws = item.Raw is null ? "" : $"raw « {Cell(item.Raw)} »";
                        string rews = item.Rewrite is null ? "" : $"rewrite « {Cell(item.Rewrite)} »";
                        string spans = string.Join(" → ", new[] { raws, rews }.Where(s => s.Length > 0));
                        md.AppendLine($"- `{category}` {spans} — {Cell(item.Note ?? "")}");
                    }
                }
                if (!any) md.AppendLine("- (no finding)");
            }
        }

        string outPath = Path.Combine(runDir, "summary.md");
        File.WriteAllText(outPath, md.ToString());
        Console.WriteLine(md.ToString());
        Console.WriteLine($"summary written to {outPath}");
        return 0;
    }

    static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    static string Signed(double v) => v.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    // Findings are model-written text going into a markdown line.
    static string Cell(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
}

sealed record MarkerDelta(
    int RawWords, int RewriteWords, double WordRatio, double CharRatio,
    int RawNegations, int RewriteNegations, int RawNegationsNoNe, int RewriteNegationsNoNe,
    int RawHedges, int RewriteHedges,
    double NegationPer100, double NegationNoNePer100, double HedgePer100);

static partial class Markers
{
    public static MarkerDelta Delta(string raw, string rewrite)
    {
        int rw = Words.Count(raw), ww = Words.Count(rewrite);
        string r = Normalise(raw), w = Normalise(rewrite);
        int rn = NegationRegex().Matches(r).Count, wn = NegationRegex().Matches(w).Count;
        int rne = NeRegex().Matches(r).Count, wne = NeRegex().Matches(w).Count;
        int rh = HedgeRegex().Matches(r).Count, wh = HedgeRegex().Matches(w).Count;
        double Per100(int count, int words) => words == 0 ? 0 : 100.0 * count / words;
        return new MarkerDelta(
            rw, ww,
            rw == 0 ? 0 : (double)ww / rw,
            raw.Length == 0 ? 0 : (double)rewrite.Length / raw.Length,
            rn, wn, rn - rne, wn - wne, rh, wh,
            Per100(wn, ww) - Per100(rn, rw),
            Per100(wn - wne, ww) - Per100(rn - rne, rw),
            Per100(wh, ww) - Per100(rh, rw));
    }

    // Lower case and one apostrophe: ASR output and model output disagree on
    // ' versus ’, and a marker must count the same either way.
    static string Normalise(string text) => text.ToLowerInvariant().Replace('’', '\'');

    // "plus" also counts its non-negative uses (« de plus en plus »): the
    // delta between raw and rewrite cancels most of them, which is why the
    // column is a delta and not an absolute count.
    // n' is elided onto the next word, so it takes no right boundary.
    [GeneratedRegex(@"(?<![\p{L}'])(?:n'|(?:ne|pas|jamais|aucun|aucune|plus|moins)(?!\p{L}))")]
    private static partial Regex NegationRegex();

    [GeneratedRegex(@"(?<![\p{L}'])(?:n'|ne(?!\p{L}))")]
    private static partial Regex NeRegex();

    [GeneratedRegex(@"(?<![\p{L}'])(je pense|je crois|peut[- ]être|je (ne |n')?sais pas|il me semble|j'ai l'impression)(?!\p{L})")]
    private static partial Regex HedgeRegex();
}
