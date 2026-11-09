using System.Text.Json;
using Anthropic;
using Deckle.Llm;

namespace Deckle.Benchmark.ModelCompare;

// ─── run: generate every cell n times, judge in batches, export ──────────────
//
// Resumable by construction: every paid call is appended to its JSONL file the
// moment it returns, and a restart reads those files first and skips what is
// already there (a row with an error is redone). A crash, a rate limit or a
// closed terminal costs nothing already paid.
//
// Parallelism is one lane per model, sequential inside a lane: the latency
// column of one model is never inflated by that model's own queue, and three
// lanes keep the wall clock near a third of the sequential run. The judge runs
// the same way, one lane per model.
//
// Nothing here changes how a rewrite is produced: the generate step calls the
// production ClaudeEngine exactly as the first study did.

static class CampaignRun
{
    public static int Run(string campaignPath, string? outDir, string? only, string apiKey)
    {
        var campaign = CampaignFile.Load(campaignPath);
        outDir ??= Path.Combine("results", $"{DateTime.Now:yyyyMMdd-HHmmss}-{campaign.Name}");
        Directory.CreateDirectory(outDir);
        var raw = Corpus.LoadRaw();
        var resolved = CampaignFile.Resolve(campaign, outDir, raw);

        Console.WriteLine($"campaign={campaign.Name} models={string.Join(",", campaign.Models)} iterations={campaign.Iterations} judge={campaign.Judge.Model} batch={campaign.Judge.Batch}");
        foreach (var (bucket, ids) in resolved.Dictations)
            Console.WriteLine($"  {bucket,-10} {string.Join(", ", ids.Select(id => $"{Judge.Short(id)} ({raw[id].WordsIn} w)"))}");
        foreach (var cell in campaign.Cells)
            Console.WriteLine($"  {cell.Key,-20} on {string.Join("+", cell.Buckets)}  prompt {resolved.Prompts[cell.Key].Hash} ({resolved.Prompts[cell.Key].SystemPrompt.Length} chars)");
        Console.WriteLine($"-> {Path.GetFullPath(outDir)}");
        Console.WriteLine();

        bool doGenerate = only is null or "generate";
        bool doJudge = only is null or "judge";
        bool doExport = only is null or "export";

        if (doGenerate) Generate(resolved, raw, outDir, apiKey);
        if (doJudge) JudgeAll(resolved, raw, outDir, apiKey);
        if (doExport) Export.Run(outDir);
        return 0;
    }

    // The three steps on a matrix already frozen in outDir (Optimize.cs
    // builds one per round): same files, same resumability, same export.
    public static void RunFrozen(ResolvedCampaign resolved, Dictionary<string, RawDictation> raw, string outDir, string apiKey)
    {
        Generate(resolved, raw, outDir, apiKey);
        JudgeAll(resolved, raw, outDir, apiKey);
        Export.Run(outDir);
    }

    // ── generate ─────────────────────────────────────────────────────────────

    sealed record WorkItem(RawDictation Dictation, FrozenPrompt Prompt, string Model, int Iteration);

    static void Generate(ResolvedCampaign resolved, Dictionary<string, RawDictation> raw, string outDir, string apiKey)
    {
        string path = Path.Combine(outDir, "generations.jsonl");
        var done = new HashSet<string>(
            File.Exists(path)
                ? Json.ReadLines<GenerationRow>(path).Where(g => g.Error is null).Select(GenKey)
                : [],
            StringComparer.Ordinal);

        var items = new List<WorkItem>();
        foreach (var cell in resolved.Campaign.Cells)
        foreach (string bucket in cell.Buckets)
        foreach (string id in resolved.Dictations[bucket])
        foreach (string model in resolved.Campaign.Models)
        for (int iteration = 1; iteration <= resolved.Campaign.Iterations; iteration++)
            items.Add(new WorkItem(raw[id], resolved.Prompts[cell.Key], model, iteration));

        var pending = items.Where(i => !done.Contains(GenKey(i))).ToList();
        Console.WriteLine($"generate: {items.Count} calls planned, {items.Count - pending.Count} already done, {pending.Count} to run");

        // Inside a lane the iterations of one cell run back to back with the
        // same request, so the first writes the cache and the others read it
        // well within its five minutes. A single iteration would only pay the
        // write, so it runs uncached.
        bool cachePrompt = resolved.Campaign.Iterations > 1;

        var engine = new ClaudeEngine(() => apiKey);
        var writer = new StreamWriter(path, append: true);
        var writeLock = new object();
        double total = 0;
        int finished = 0;

        var lanes = resolved.Campaign.Models.Select(model => Task.Run(() =>
        {
            foreach (var item in pending.Where(i => i.Model == model))
            {
                var row = GenerateOne(engine, item, cachePrompt);
                lock (writeLock)
                {
                    writer.WriteLine(JsonSerializer.Serialize(row, Json.Output));
                    writer.Flush();
                    total += row.CostUsd ?? 0;
                    finished++;
                    Console.WriteLine(row.Error is null
                        ? $"[{finished}/{pending.Count}] {Judge.Short(row.Id)} {row.Model,-18} {row.Prompt,-20} #{row.Iteration,-2} tok={row.PromptTokens}/{row.EvalTokens} cache={row.CacheReadTokens}r/{row.CacheWriteTokens}w {row.TotalMs}ms ${row.CostUsd:F4} words={row.WordsIn}->{row.WordsOut}"
                        : $"[{finished}/{pending.Count}] {Judge.Short(row.Id)} {row.Model,-18} {row.Prompt,-20} #{row.Iteration,-2} ERROR {row.Error}");
                }
            }
        })).ToArray();
        Task.WaitAll(lanes);
        writer.Dispose();

        Console.WriteLine();
        Console.WriteLine($"generation cost this run: ${total:F4}");
        Console.WriteLine();
    }

    static GenerationRow GenerateOne(ClaudeEngine engine, WorkItem item, bool cachePrompt)
    {
        var request = new GenerationRequest(
            Model:        item.Model,
            SystemPrompt: item.Prompt.SystemPrompt,
            UserText:     item.Dictation.Text,
            Label:        "benchmark",
            CachePrompt:  cachePrompt);

        GenerationResult? result = null;
        string? error = null;
        for (int attempt = 1; attempt <= 2 && result is null; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try
            {
                result = engine.Generate(request, cts.Token);
            }
            catch (Exception ex) when (attempt == 1 && Transient.Is(ex))
            {
                Console.WriteLine($"  transient failure, retrying once: {Transient.Describe(ex)}");
            }
            catch (Exception ex)
            {
                error = Transient.Describe(ex);
            }
        }

        var d = item.Dictation;
        return result is { } r
            ? new GenerationRow(
                Id: d.Id, Bucket: d.Bucket, WordsIn: d.WordsIn, WordsOut: Words.Count(r.Text),
                Model: item.Model, Prompt: item.Prompt.Name, Source: "benchmark", Text: r.Text,
                PromptTokens: r.PromptTokens, EvalTokens: r.EvalTokens, TotalMs: r.TotalMs,
                CostUsd: Pricing.Cost(item.Model, r.PromptTokens, r.EvalTokens, r.CacheWriteTokens, r.CacheReadTokens), Error: null,
                Iteration: item.Iteration, CacheWriteTokens: r.CacheWriteTokens, CacheReadTokens: r.CacheReadTokens)
            : new GenerationRow(
                Id: d.Id, Bucket: d.Bucket, WordsIn: d.WordsIn, WordsOut: 0,
                Model: item.Model, Prompt: item.Prompt.Name, Source: "benchmark", Text: null,
                PromptTokens: null, EvalTokens: null, TotalMs: null, CostUsd: null, Error: error ?? "no result",
                Iteration: item.Iteration);
    }

    static string GenKey(WorkItem i) => $"{i.Dictation.Id}|{i.Model}|{i.Prompt.Name}|{i.Iteration}";
    public static string GenKey(GenerationRow g) => $"{g.Id}|{g.Model}|{g.Prompt}|{g.Iteration}";

    // ── judge ────────────────────────────────────────────────────────────────

    static void JudgeAll(ResolvedCampaign resolved, Dictionary<string, RawDictation> raw, string outDir, string apiKey)
    {
        string genPath = Path.Combine(outDir, "generations.jsonl");
        string batchPath = Path.Combine(outDir, "judge-batches.jsonl");
        string flatPath = Path.Combine(outDir, "judgements.jsonl");
        if (!File.Exists(genPath)) { Console.WriteLine("judge: nothing generated yet"); return; }

        // The last successful row per key wins: a redone generation replaces
        // its failed attempt without anyone editing the file.
        var generations = Json.ReadLines<GenerationRow>(genPath)
            .Where(g => g.Error is null && !string.IsNullOrWhiteSpace(g.Text))
            .GroupBy(GenKey).Select(g => g.Last())
            .ToList();

        var done = new HashSet<string>(
            File.Exists(batchPath)
                ? Json.ReadLines<BatchJudgementRow>(batchPath).Where(b => b.Error is null).Select(b => b.Key)
                : [],
            StringComparer.Ordinal);

        var profileNotes = resolved.Campaign.Cells
            .ToDictionary(c => resolved.Prompts[c.Key].Name, c => c.JudgeNote, StringComparer.OrdinalIgnoreCase);

        int batchSize = resolved.Campaign.Judge.Batch;
        var batches = new List<(string Key, List<GenerationRow> Rows)>();
        foreach (var cell in generations.GroupBy(g => (g.Id, g.Model, g.Prompt)))
        {
            var ordered = cell.OrderBy(g => g.Iteration ?? 0).ToList();
            for (int i = 0; i < ordered.Count; i += batchSize)
            {
                var rows = ordered.Skip(i).Take(batchSize).ToList();
                string key = $"{cell.Key.Id}|{cell.Key.Model}|{cell.Key.Prompt}|{rows[0].Iteration}-{rows[^1].Iteration}";
                batches.Add((key, rows));
            }
        }
        var pending = batches.Where(b => !done.Contains(b.Key)).ToList();
        Console.WriteLine($"judge: {batches.Count} batches planned, {batches.Count - pending.Count} already done, {pending.Count} to run");

        var client = new AnthropicClient { ApiKey = apiKey };
        var batchWriter = new StreamWriter(batchPath, append: true);
        var flatWriter = new StreamWriter(flatPath, append: true);
        var writeLock = new object();
        double total = 0;
        int finished = 0;

        // The judge's latency measures nothing, so four cells are judged at a
        // time inside a lane: a long cell's judging no longer takes longer
        // than its generation. The batches of one cell run one after the
        // other: they share the raw dictation, and the cache is readable only
        // once the call that writes it has answered, so batches sent together
        // would each pay to write it and none would read it.
        var lanes = resolved.Campaign.Models.Select(model => Task.Run(() =>
        {
            var cells = pending.Where(b => b.Rows[0].Model == model)
                .GroupBy(b => (b.Rows[0].Id, b.Rows[0].Prompt))
                .ToList();
            Parallel.ForEach(cells, new ParallelOptions { MaxDegreeOfParallelism = 4 }, cell =>
            {
                foreach (var (key, rows) in cell)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
                    profileNotes.TryGetValue(rows[0].Prompt, out string? note);
                    var batch = BatchJudge.JudgeBatch(client, resolved.Campaign.Judge.Model, note, key, raw[rows[0].Id].Text, rows, cts.Token);
                    lock (writeLock)
                    {
                        batchWriter.WriteLine(JsonSerializer.Serialize(batch, Json.Output));
                        batchWriter.Flush();
                        foreach (var flat in Flatten(batch, rows))
                            flatWriter.WriteLine(JsonSerializer.Serialize(flat, Json.Output));
                        flatWriter.Flush();
                        total += batch.JudgeCostUsd ?? 0;
                        finished++;
                        string outcome = batch.Error ?? string.Join("/", batch.Judgements!.Select(j => j?.Verdict?[..Math.Min(3, j.Verdict.Length)] ?? "?"));
                        Console.WriteLine($"[{finished}/{pending.Count}] {Judge.Short(rows[0].Id)} {model,-18} {rows[0].Prompt,-20} #{rows[0].Iteration}-{rows[^1].Iteration} {outcome,-22} tok={batch.JudgePromptTokens}/{batch.JudgeEvalTokens} cache={batch.JudgeCacheReadTokens}r/{batch.JudgeCacheWriteTokens}w {batch.JudgeMs}ms ${batch.JudgeCostUsd:F4}");
                    }
                }
            });
        })).ToArray();
        Task.WaitAll(lanes);
        batchWriter.Dispose();
        flatWriter.Dispose();

        Console.WriteLine();
        Console.WriteLine($"judge cost this run: ${total:F4}");
        Console.WriteLine();
    }

    // One JudgementRow per rewrite, so Summary and Export read a batch run the
    // way they read a one-by-one run; the batch's tokens, time and cost are
    // split evenly, which is the only split the API gives.
    static IEnumerable<JudgementRow> Flatten(BatchJudgementRow batch, List<GenerationRow> rows)
    {
        int n = rows.Count;
        for (int i = 0; i < n; i++)
        {
            var g = rows[i];
            var judgement = batch.Judgements?[i];
            string? error = batch.Error ?? (judgement is null && batch.Judgements is not null ? "missing in batch answer" : null);
            yield return new JudgementRow(
                g.Id, g.Model, g.Prompt, g.Source, batch.JudgeModel, judgement, i == 0 ? batch.JudgeRaw : null,
                batch.JudgePromptTokens / n, batch.JudgeEvalTokens / n, batch.JudgeMs / n, batch.JudgeCostUsd / n,
                error, g.Iteration, batch.Key,
                batch.JudgeCacheWriteTokens / n, batch.JudgeCacheReadTokens / n);
        }
    }
}
