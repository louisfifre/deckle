using System.Text.Json;
using System.Text.RegularExpressions;

namespace Deckle.Benchmark.ModelCompare;

// ─── export: one JSON file a dashboard can be built from ─────────────────────
//
// The dashboard is made elsewhere, by a later session, from this file alone:
// it must therefore carry everything that session needs, including the texts
// (the judge is a lead; the texts are the proof) and the metrics computed in
// code here, so no reader has to recompute a ratio or re-parse a judgement.
//
// Shape (snake_case):
//   campaign      the frozen matrix: models, iterations, cells, judge, the
//                 dictation ids per bucket and the prompts with their hash
//   pricing       USD per MTok per model, as Shared.cs holds it
//   dictations    id, bucket, words, chars, text
//   generations   one per (dictation, profile, model, iteration): the text,
//                 tokens, latency, cost, code metrics, and the judgement
//                 (verdict, counts, findings, judge cost) when there is one
//   cells         one per (profile, bucket, dictation, model): n, verdict
//                 counts, finding totals, latency p50/p90/mean, cost totals,
//                 mean ratios
//   totals        calls, costs, judged count
//
// The file holds dictation text: it stays in the git-ignored results folder.

static partial class Export
{
    public static int Run(string runDir)
    {
        string frozenPath = Path.Combine(runDir, "campaign.json");
        string genPath = Path.Combine(runDir, "generations.jsonl");
        string judgePath = Path.Combine(runDir, "judgements.jsonl");
        if (!File.Exists(genPath)) throw new ArgumentException($"No generations.jsonl in {runDir}.");

        var resolved = File.Exists(frozenPath)
            ? JsonSerializer.Deserialize<ResolvedCampaign>(File.ReadAllText(frozenPath), Json.Input)
            : null;
        var raw = Corpus.LoadRaw();

        var generations = Json.ReadLines<GenerationRow>(genPath)
            .GroupBy(CampaignRun.GenKey).Select(g => g.Last()) // a redone call replaces its failed attempt
            .ToList();
        var judgements = File.Exists(judgePath)
            ? Json.ReadLines<JudgementRow>(judgePath)
                .GroupBy(j => $"{j.Id}|{j.Model}|{j.Prompt}|{j.Iteration}").Select(g => g.Last())
                .ToDictionary(j => $"{j.Id}|{j.Model}|{j.Prompt}|{j.Iteration}", StringComparer.Ordinal)
            : new Dictionary<string, JudgementRow>(StringComparer.Ordinal);

        var profileNames = resolved?.Prompts.Values.ToDictionary(p => p.Name, p => p.Profile, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string ProfileOf(string prompt) => profileNames.TryGetValue(prompt, out var name) ? name : prompt;

        var dictationIds = generations.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dictations = dictationIds
            .Where(raw.ContainsKey)
            .Select(id => raw[id])
            .Select(d => new { id = d.Id, bucket = d.Bucket, words = d.WordsIn, chars = d.Text.Length, timestamp = d.Timestamp, text = d.Text })
            .ToList();

        var genOut = new List<object>(generations.Count);
        foreach (var g in generations)
        {
            object? metrics = null;
            if (g.Error is null && g.Text is not null && raw.TryGetValue(g.Id, out var d))
            {
                var m = Markers.Delta(d.Text, g.Text);
                var s = Structure.Of(d.Text, g.Text);
                metrics = new
                {
                    word_ratio = m.WordRatio,
                    char_ratio = m.CharRatio,
                    paragraphs = s.Paragraphs,
                    separators = s.Separators,
                    markdown_marks = s.MarkdownMarks,
                    lexical_retention = s.LexicalRetention,
                    negation_delta_per100 = m.NegationPer100,
                    negation_no_ne_delta_per100 = m.NegationNoNePer100,
                    hedge_delta_per100 = m.HedgePer100,
                    raw_hedges = m.RawHedges,
                    rewrite_hedges = m.RewriteHedges,
                };
            }

            object? judgement = null;
            if (judgements.TryGetValue(CampaignRun.GenKey(g), out var j))
            {
                judgement = new
                {
                    judge_model = j.JudgeModel,
                    verdict = j.Judgement?.Verdict,
                    counts = j.Judgement is null ? null : j.Judgement.Categories().ToDictionary(c => c.Category, c => c.Items.Count),
                    findings = j.Judgement is null ? null : j.Judgement.Categories().ToDictionary(c => c.Category, c => c.Items),
                    judge_prompt_tokens = j.JudgePromptTokens,
                    judge_eval_tokens = j.JudgeEvalTokens,
                    judge_ms = j.JudgeMs,
                    judge_cost_usd = j.JudgeCostUsd,
                    batch = j.Batch,
                    error = j.Error,
                };
            }

            genOut.Add(new
            {
                id = g.Id,
                bucket = g.Bucket,
                profile = ProfileOf(g.Prompt),
                prompt = g.Prompt,
                model = g.Model,
                reasoning = ModelVariant.LevelName(g.Model),
                iteration = g.Iteration,
                source = g.Source,
                words_in = g.WordsIn,
                words_out = g.WordsOut,
                prompt_tokens = g.PromptTokens,
                cache_write_tokens = g.CacheWriteTokens,
                cache_read_tokens = g.CacheReadTokens,
                cache_hit = g.CacheReadTokens > 0,
                eval_tokens = g.EvalTokens,
                total_ms = g.TotalMs,
                cost_usd = g.CostUsd,
                error = g.Error,
                text = g.Text,
                metrics,
                judgement,
            });
        }

        // Grouped by prompt as well as profile: a campaign can run several
        // wordings of one profile side by side, and their cells must not merge.
        var cells = generations
            .GroupBy(g => (Profile: ProfileOf(g.Prompt), g.Prompt, g.Bucket, g.Id, g.Model))
            .OrderBy(c => c.Key.Profile).ThenBy(c => c.Key.Prompt).ThenBy(c => c.Key.Bucket).ThenBy(c => c.Key.Id).ThenBy(c => c.Key.Model)
            .Select(c =>
            {
                var ok = c.Where(g => g.Error is null).ToList();
                var judged = ok.Select(g => judgements.TryGetValue(CampaignRun.GenKey(g), out var j) ? j : null)
                    .Where(j => j?.Judgement is not null).Select(j => j!.Judgement!).ToList();
                var ms = ok.Where(g => g.TotalMs is not null).Select(g => (double)g.TotalMs!.Value).OrderBy(v => v).ToList();
                // A row that read the cache skipped the prompt's processing:
                // the cold rows alone are comparable with the app.
                var coldMs = ok.Where(g => g.TotalMs is not null && !(g.CacheReadTokens > 0)).Select(g => (double)g.TotalMs!.Value).OrderBy(v => v).ToList();
                var ratios = ok.Where(g => raw.ContainsKey(g.Id) && g.Text is not null).Select(g => Markers.Delta(raw[g.Id].Text, g.Text!).WordRatio).ToList();
                int Verdicts(string v) => judged.Count(j => string.Equals(j.Verdict, v, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    profile = c.Key.Profile,
                    prompt = c.Key.Prompt,
                    bucket = c.Key.Bucket,
                    id = c.Key.Id,
                    model = c.Key.Model,
                    reasoning = ModelVariant.LevelName(c.Key.Model),
                    n = c.Count(),
                    ok = ok.Count,
                    errors = c.Count() - ok.Count,
                    judged = judged.Count,
                    verdicts = new { faithful = Verdicts("faithful"), minor = Verdicts("minor"), unfaithful = Verdicts("unfaithful") },
                    findings = judged.Count == 0 ? null : new Dictionary<string, double>
                    {
                        ["term_substitutions"] = judged.Average(j => j.TermSubstitutions?.Count ?? 0),
                        ["meaning_inversions"] = judged.Average(j => j.MeaningInversions?.Count ?? 0),
                        ["hedges_dropped"] = judged.Average(j => j.HedgesDropped?.Count ?? 0),
                        ["omissions"] = judged.Average(j => j.Omissions?.Count ?? 0),
                        ["additions"] = judged.Average(j => j.Additions?.Count ?? 0),
                    },
                    latency_ms = ms.Count == 0 ? null : new { mean = ms.Average(), p50 = Percentile(ms, 0.5), p90 = Percentile(ms, 0.9), min = ms[0], max = ms[^1] },
                    cache_hits = ok.Count(g => g.CacheReadTokens > 0),
                    latency_cold_ms = coldMs.Count == 0 ? null : new { n = coldMs.Count, mean = coldMs.Average(), p50 = Percentile(coldMs, 0.5), p90 = Percentile(coldMs, 0.9), min = coldMs[0], max = coldMs[^1] },
                    cost_usd = new
                    {
                        generation_total = ok.Sum(g => g.CostUsd ?? 0),
                        generation_mean = ok.Count == 0 ? 0 : ok.Average(g => g.CostUsd ?? 0),
                        judge_total = ok.Sum(g => judgements.TryGetValue(CampaignRun.GenKey(g), out var j) ? j.JudgeCostUsd ?? 0 : 0),
                    },
                    word_ratio_mean = ratios.Count == 0 ? (double?)null : ratios.Average(),
                    eval_tokens_mean = ok.Count == 0 ? (double?)null : ok.Average(g => g.EvalTokens ?? 0),
                };
            })
            .ToList();

        var totals = new
        {
            calls = generations.Count,
            errors = generations.Count(g => g.Error is not null),
            judged = judgements.Values.Count(j => j.Judgement is not null),
            generation_cost_usd = generations.Sum(g => g.CostUsd ?? 0),
            judge_cost_usd = judgements.Values.Sum(j => j.JudgeCostUsd ?? 0),
            exported_at = DateTimeOffset.Now,
        };

        var doc = new
        {
            campaign = resolved,
            pricing = Pricing.Snapshot(),
            dictations,
            generations = genOut,
            cells,
            totals,
        };

        string outPath = Path.Combine(runDir, "dashboard-data.json");
        File.WriteAllText(outPath, JsonSerializer.Serialize(doc, Json.Pretty));
        Console.WriteLine($"export: {generations.Count} generations, {totals.judged} judged, gen ${totals.generation_cost_usd:F2} + judge ${totals.judge_cost_usd:F2}");
        Console.WriteLine($"-> {Path.GetFullPath(outPath)}");
        return 0;
    }

    static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double rank = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}

// Shape metrics a reader can trust to be the same on every rerun: how the
// rewrite is laid out, and how much of the speaker's vocabulary it kept.
sealed record StructureMetrics(int Paragraphs, int Separators, int MarkdownMarks, double LexicalRetention);

static partial class Structure
{
    public static StructureMetrics Of(string raw, string rewrite)
    {
        string text = rewrite.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        int paragraphs = Regex.Split(text.Trim(), @"\n\s*\n").Count(p => p.Trim().Length > 0);
        int separators = lines.Count(l => SeparatorRegex().IsMatch(l));
        int marks = lines.Count(l => MarkdownLineRegex().IsMatch(l)) + Regex.Matches(text, @"\*\*").Count / 2;

        // Content-word types (4+ letters, lower-cased) of the raw that the
        // rewrite still contains: a profile that must keep the speaker's
        // words scores near 1, a paraphrasing one drops.
        var rawTypes = WordRegex().Matches(raw).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
        var rewriteTypes = WordRegex().Matches(rewrite).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
        double retention = rawTypes.Count == 0 ? 1 : (double)rawTypes.Count(rewriteTypes.Contains) / rawTypes.Count;

        return new StructureMetrics(paragraphs, separators, marks, retention);
    }

    [GeneratedRegex(@"^\s*(-{3,}|\*{3,}|_{3,})\s*$")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"^\s*(#{1,6}\s|[-*+]\s|\d+[.)]\s|>\s)")]
    private static partial Regex MarkdownLineRegex();

    [GeneratedRegex(@"\p{L}{4,}")]
    private static partial Regex WordRegex();
}
