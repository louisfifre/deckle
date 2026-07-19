using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Llm;
using Deckle.Llm.Rewrite;

// ─── ParagraphGate — prompt-sample eval of the retaille service + diff gate ──
//
// Slice 1 of the paragraph rewrite ("Retaille de paragraphe"): before any
// UI or wiring exists, burn the core risk — does a Ministral-class model at
// temperature 0, behind the real ParagraphRewrite prompt, produce rewrites
// the diff gate can accept, at an offer-compatible latency?
//
// The study exercises the REAL production code end to end: the prompt's
// single home (ParagraphRewrite), the engine seam (OllamaEngine), the gate
// (RewriteDiffGate). Nothing is reimplemented here; the harness only feeds
// samples, times, and counts.
//
// Sequential by design — one local GPU, heavy jobs never run in parallel.
//
// Usage (from this folder):
//   dotnet run -c Release [-- --model <name>] [--endpoint <url>]
//                         [--samples <path>] [--timeout-s <n>]
// Model default: first local model whose name contains "ministral", then
// "mistral". Results: results/<stamp>-<model>.jsonl next to the samples,
// aggregates on the console.

string endpoint = Arg("--endpoint") ?? "http://localhost:11434/api/generate";
string? modelArg = Arg("--model");
string samplesPath = Arg("--samples") ?? FindDefaultSamples();
int timeoutS = int.TryParse(Arg("--timeout-s"), out int parsedTimeout) ? parsedTimeout : 120;

// Prompt-iteration loop: --prompt-file swaps the system prompt without
// recompiling src. The shipped ParagraphRewrite.SystemPrompt stays the
// reference; a variant that wins here is then promoted into its single home.
string? promptFile = Arg("--prompt-file");
string? promptOverride = promptFile is null ? null : File.ReadAllText(promptFile).Trim();

var ollama = new OllamaService(() => endpoint);

// The retry path IsAvailableAsync documents for engine warm-up gets a real
// caller here: an eval run right after boot should wait out the race, not
// fail it.
if (!await ollama.IsAvailableAsync(maxAttempts: 3, retryDelay: TimeSpan.FromSeconds(1)))
{
    Console.Error.WriteLine($"Ollama unreachable at {endpoint} — start it and rerun.");
    return 1;
}

var models = await ollama.ListModelsAsync();
string? model = modelArg
    ?? models.FirstOrDefault(m => m.Name.Contains("ministral", StringComparison.OrdinalIgnoreCase))?.Name
    ?? models.FirstOrDefault(m => m.Name.Contains("mistral", StringComparison.OrdinalIgnoreCase))?.Name;
if (model is null)
{
    Console.Error.WriteLine("No Ministral/Mistral-class model found locally. Available:");
    foreach (var m in models) Console.Error.WriteLine($"  {m.Name}");
    Console.Error.WriteLine("Pick one with --model <name>.");
    return 2;
}

var samples = File.ReadLines(samplesPath)
    .Where(l => !string.IsNullOrWhiteSpace(l))
    .Select(l => JsonSerializer.Deserialize<Sample>(l, Json.Input)!)
    .ToList();

Console.WriteLine($"model={model} endpoint={endpoint} samples={samples.Count} timeout={timeoutS}s"
    + (promptFile is null ? "" : $" prompt={Path.GetFileName(promptFile)}"));
Console.WriteLine();

RewriteEngineRequest BuildRequest(string paragraph)
{
    var request = ParagraphRewrite.BuildRequest(paragraph, endpoint, model!);
    return promptOverride is null ? request : request with { SystemPrompt = promptOverride };
}

var engine = new OllamaEngine();

// Warm-up prime: pays the model load once, measured but kept out of the
// aggregates — the offer-latency question is about the warm path (the
// framed trigger warms the model opportunistically while a paragraph
// accumulates).
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(timeoutS, 300)));
    var prime = engine.Generate(BuildRequest("ca marche"), cts.Token);
    Console.WriteLine($"warm-up: total={prime.TotalMs}ms load={prime.OllamaLoadMs}ms");
    Console.WriteLine();
}

var rows = new List<Row>();
foreach (var sample in samples)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutS));
    RewriteResult result;
    try
    {
        result = engine.Generate(BuildRequest(sample.Text), cts.Token);
    }
    catch (Exception ex)
    {
        rows.Add(Row.Failed(sample, ex is OperationCanceledException ? "timeout" : ex.GetType().Name));
        Console.WriteLine($"{sample.Id,-24} ERROR   {rows[^1].Error}");
        continue;
    }

    var verdict = RewriteDiffGate.Evaluate(sample.Text, result.Text ?? "");
    string outcome = !verdict.Accepted ? "reject" : verdict.IsIdentity ? "identity" : "offer";
    bool expectMet = sample.Expect switch
    {
        "offer" => outcome == "offer",
        "identity" => outcome == "identity",
        _ => true, // "open": no committed expectation
    };

    var violations = verdict.Edits
        .Where(e => !e.IsAllowed)
        .Select(e => new ViolationRow(e.Ruling.ToString(), e.Original, e.Rewritten))
        .ToList();

    double ratio = sample.Text.Length > 0 ? (double)(result.Text?.Length ?? 0) / sample.Text.Length : 0;
    rows.Add(new Row(
        Id: sample.Id, Expect: sample.Expect, Outcome: outcome, ExpectMet: expectMet,
        TotalMs: result.TotalMs, LoadMs: result.OllamaLoadMs,
        PromptTokens: result.PromptTokens, EvalTokens: result.EvalTokens,
        LengthRatio: Math.Round(ratio, 2),
        Violations: violations,
        Text: sample.Text, Rewritten: result.Text ?? "", Error: null));

    string flag = expectMet ? "  " : " !";
    string firstViolation = violations.Count > 0
        ? $"  [{violations[0].Ruling}] \"{violations[0].Original}\" -> \"{violations[0].Rewritten}\""
        : "";
    Console.WriteLine($"{sample.Id,-24} {outcome,-8}{flag} {result.TotalMs,6}ms  ratio={ratio:F2}{firstViolation}");
}

// ── Aggregates (errors and warm-up excluded from latency) ────────────────────

var measured = rows.Where(r => r.Error is null).ToList();
var latencies = measured.Select(r => r.TotalMs).OrderBy(v => v).ToList();
int offers = measured.Count(r => r.Outcome == "offer");
int identities = measured.Count(r => r.Outcome == "identity");
int rejects = measured.Count(r => r.Outcome == "reject");
int misses = rows.Count(r => !r.ExpectMet);

Console.WriteLine();
Console.WriteLine($"outcomes: offer={offers} identity={identities} reject={rejects} error={rows.Count - measured.Count}  (expectations missed: {misses})");
if (latencies.Count > 0)
{
    Console.WriteLine($"latency:  p50={Percentile(latencies, 50)}ms p95={Percentile(latencies, 95)}ms max={latencies[^1]}ms");
}
var reasonHistogram = measured
    .SelectMany(r => r.Violations)
    .GroupBy(v => v.Ruling)
    .OrderByDescending(g => g.Count());
foreach (var group in reasonHistogram)
    Console.WriteLine($"rejects:  {group.Key} x{group.Count()}");

// ── Persist the run ──────────────────────────────────────────────────────────

string resultsDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(samplesPath))!, "results");
Directory.CreateDirectory(resultsDir);
string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
string safeModel = string.Join("-", model.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Replace(':', '-');
string outPath = Path.Combine(resultsDir, $"{stamp}-{safeModel}.jsonl");
await using (var writer = new StreamWriter(outPath))
{
    foreach (var row in rows)
        await writer.WriteLineAsync(JsonSerializer.Serialize(row, Json.Output));
}
Console.WriteLine();
Console.WriteLine($"rows written to {outPath}");
return 0;

string? Arg(string name)
{
    string[] argv = Environment.GetCommandLineArgs();
    for (int i = 0; i < argv.Length - 1; i++)
        if (string.Equals(argv[i], name, StringComparison.Ordinal))
            return argv[i + 1];
    return null;
}

// `dotnet run` from the study folder finds the source-tree samples (so
// results land next to them); a bare exe falls back to the copied file.
string FindDefaultSamples()
{
    string cwd = Path.Combine(Environment.CurrentDirectory, "samples.jsonl");
    return File.Exists(cwd) ? cwd : Path.Combine(AppContext.BaseDirectory, "samples.jsonl");
}

static long Percentile(List<long> sorted, int percent)
{
    int index = (int)Math.Ceiling(percent / 100.0 * sorted.Count) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
}

sealed record Sample(string Id, string Expect, string Note, string Text);

sealed record ViolationRow(string Ruling, string Original, string Rewritten);

sealed record Row(
    string Id, string Expect, string Outcome, bool ExpectMet,
    long TotalMs, long LoadMs, int PromptTokens, int EvalTokens,
    double LengthRatio, List<ViolationRow> Violations,
    string Text, string Rewritten, string? Error)
{
    public static Row Failed(Sample sample, string error) => new(
        sample.Id, sample.Expect, "error", false, 0, 0, 0, 0, 0,
        new List<ViolationRow>(), sample.Text, "", error);
}

file static class Json
{
    public static readonly JsonSerializerOptions Input = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static readonly JsonSerializerOptions Output = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
