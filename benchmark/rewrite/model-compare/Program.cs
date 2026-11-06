using System.Text.Json;
using Deckle.Benchmark.ModelCompare;
using Deckle.Llm;
using Deckle.Security;

// ─── ModelCompare — Claude models × prompt variants on the whole-dictation rewrite
//
// The production "Lissage" profile rewrites a whole dictation through Claude.
// Which model, and which wording of the system prompt, keeps the speaker's
// meaning best for the money? This study answers on real dictations already
// recorded in Deckle's telemetry corpus, in three steps:
//
//   generate  runs the model × prompt matrix through the REAL engine
//             (ClaudeEngine, effort low — the production path), and can pull
//             the rewrites Deckle already produced for the same dictations;
//   judge     has a stronger reasoning call compare each raw/rewrite pair and
//             list meaning changes as JSON;
//   summary   aggregates both, adds marker counts computed in code, and lists
//             every finding so a person can read the actual spans.
//
// Sequential by design: one call at a time keeps the latency column honest
// and the spend easy to stop.
//
// Usage (from this folder, after `dotnet build /m:4`):
//   bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ModelCompare.exe generate
//       --models claude-opus-5-5,claude-sonnet-5-5
//       --prompts prompts/lissage-current.md,prompts/lissage-literal.md
//       [--profile Lissage] (--ids a,b,c | --bucket long --take N)
//       [--include-existing] [--out results/<stamp>]
//   ModelCompare.exe judge --in results/<stamp>/generations.jsonl [--judge-model claude-opus-5-5]
//   ModelCompare.exe summary --in results/<stamp>
// Results: results/<stamp>/{generations,judgements}.jsonl and summary.md,
// git-ignored — they hold dictation text, which never enters the repository.
//
// A campaign (Campaign.cs) runs the whole matrix of a study in one command,
// n iterations per cell, judged in batches, and exports the dashboard data:
//   ModelCompare.exe check
//   ModelCompare.exe run --campaign campaigns/profiles-v2.json [--out results/<stamp>] [--only generate|judge|export]
//   ModelCompare.exe export --in results/<stamp>
// run is resumable: rerun the same --out and it skips what is already paid.

string? command = args.Length > 0 ? args[0] : null;
try
{
    return command switch
    {
        "generate" => Generate(),
        "judge" => Judge.Run(
            Arg("--in") ?? throw new ArgumentException("judge needs --in results/<stamp>/generations.jsonl"),
            Arg("--judge-model") ?? "claude-opus-5-5",
            ApiKey()),
        "summary" => Summary.Run(Arg("--in") ?? throw new ArgumentException("summary needs --in results/<stamp>")),
        "check" => Check(),
        "run" => CampaignRun.Run(
            Arg("--campaign") ?? throw new ArgumentException("run needs --campaign campaigns/<name>.json"),
            Arg("--out"), Arg("--only"), ApiKey()),
        "export" => Export.Run(Arg("--in") ?? throw new ArgumentException("export needs --in results/<stamp>")),
        _ => Usage(),
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

int Generate()
{
    var models = (Arg("--models") ?? throw new ArgumentException("generate needs --models a,b"))
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var prompts = new List<PromptVariant>();
    foreach (string file in (Arg("--prompts") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        prompts.Add(Corpus.LoadPromptFile(file));
    if (Arg("--profile") is { } profileName)
        prompts.Add(Corpus.LoadProfilePrompt(profileName));
    if (prompts.Count == 0)
        throw new ArgumentException("generate needs --prompts <file>[,<file>] and/or --profile <name>");

    var raw = Corpus.LoadRaw();
    var selected = Corpus.Select(raw, Arg("--ids"), Arg("--bucket"),
        int.TryParse(Arg("--take"), out int take) ? take : 5);
    bool includeExisting = args.Contains("--include-existing");

    string outDir = Arg("--out") ?? Path.Combine("results", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, "generations.jsonl");

    Console.WriteLine($"dictations={selected.Count} models={string.Join(",", models)} prompts={string.Join(",", prompts.Select(p => p.Name))}"
        + (includeExisting ? " +existing" : ""));
    Console.WriteLine($"-> {Path.GetFullPath(outPath)}");
    Console.WriteLine();

    string key = ApiKey();
    var engine = new ClaudeEngine(() => key);
    double total = 0;

    using var writer = new StreamWriter(outPath, append: false);
    void Write(GenerationRow row)
    {
        writer.WriteLine(JsonSerializer.Serialize(row, Json.Output));
        writer.Flush(); // a crash mid-matrix keeps every paid generation so far
    }

    if (includeExisting)
    {
        foreach (var row in Corpus.LoadProductionRewrites(selected))
        {
            Write(row);
            Console.WriteLine($"{row.Id[..8]} {row.Model,-18} {row.Prompt,-36} deckle  words={row.WordsIn}->{row.WordsOut} {row.TotalMs}ms");
        }
    }

    foreach (var dictation in selected)
    foreach (string model in models)
    foreach (var prompt in prompts)
    {
        // Built as RewriteService builds it for a profile; the sampling hints
        // are left null because ClaudeEngine drops them anyway.
        var request = new GenerationRequest(
            Model:        model,
            SystemPrompt: prompt.SystemPrompt,
            UserText:     dictation.Text,
            Label:        "benchmark");

        GenerationResult? result = null;
        string? error = null;
        for (int attempt = 1; attempt <= 2 && result is null; attempt++)
        {
            // The app's own hard cap is minutes; ten covers a slow long
            // dictation without hanging the matrix on a stuck call.
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

        GenerationRow row = result is { } r
            ? new GenerationRow(
                Id: dictation.Id, Bucket: dictation.Bucket, WordsIn: dictation.WordsIn, WordsOut: Words.Count(r.Text),
                Model: model, Prompt: prompt.Name, Source: "benchmark", Text: r.Text,
                PromptTokens: r.PromptTokens, EvalTokens: r.EvalTokens, TotalMs: r.TotalMs,
                CostUsd: Pricing.Cost(model, r.PromptTokens, r.EvalTokens), Error: null)
            : new GenerationRow(
                Id: dictation.Id, Bucket: dictation.Bucket, WordsIn: dictation.WordsIn, WordsOut: 0,
                Model: model, Prompt: prompt.Name, Source: "benchmark", Text: null,
                PromptTokens: null, EvalTokens: null, TotalMs: null, CostUsd: null, Error: error ?? "no result");
        Write(row);
        total += row.CostUsd ?? 0;

        Console.WriteLine(row.Error is null
            ? $"{row.Id[..8]} {model,-18} {prompt.Name,-36} tok={row.PromptTokens}/{row.EvalTokens} {row.TotalMs}ms ${row.CostUsd:F4}  words={row.WordsIn}->{row.WordsOut}"
            : $"{row.Id[..8]} {model,-18} {prompt.Name,-36} ERROR {row.Error}");
    }

    Console.WriteLine();
    Console.WriteLine($"generation cost: ${total:F4}");
    return 0;
}

// Does the stored key open the API? Lists the models the key can reach, the
// cheapest call there is (no tokens billed), and says which of the candidate
// models are served. The key itself is never shown.
int Check()
{
    string key;
    try { key = ApiKey(); }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"key: none ({ex.Message})");
        return 1;
    }
    bool fromVault = SecretVault.CreateDefault().TryGet(LlmProviderSecrets.ApiKeyName("claude"), out string? vaultKey)
        && !string.IsNullOrWhiteSpace(vaultKey);
    Console.WriteLine($"key: present ({(fromVault ? "Deckle vault, provider 'claude'" : "ANTHROPIC_API_KEY")}, {key.Length} chars)");

    var client = new Anthropic.AnthropicClient { ApiKey = key };
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var page = client.Models.List(new Anthropic.Models.Models.ModelListParams()).GetAwaiter().GetResult();
    sw.Stop();
    var ids = page.Items.Select(m => m.ID).ToList();
    Console.WriteLine($"models: {ids.Count} reachable in {sw.ElapsedMilliseconds} ms");
    foreach (string candidate in new[] { "claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5", "claude-fable-5-1" })
        Console.WriteLine($"  {candidate,-18} {(ids.Contains(candidate) ? "served" : "NOT served")}");
    return 0;
}

// The key Deckle itself uses for its "claude" provider, from the DPAPI vault;
// ANTHROPIC_API_KEY only as a fallback for a machine without the app's vault.
// The key is held in memory only: never printed, never written.
string ApiKey()
{
    if (SecretVault.CreateDefault().TryGet(LlmProviderSecrets.ApiKeyName("claude"), out string? key)
        && !string.IsNullOrWhiteSpace(key))
        return key;
    return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } env
        ? env
        : throw new ArgumentException("No Claude API key: none in Deckle's vault for provider 'claude', and ANTHROPIC_API_KEY is unset.");
}

int Usage()
{
    Console.Error.WriteLine("usage: ModelCompare check|run|export|generate|judge|summary [options] — see README.md");
    return 1;
}

string? Arg(string name)
{
    string[] argv = Environment.GetCommandLineArgs();
    for (int i = 0; i < argv.Length - 1; i++)
        if (string.Equals(argv[i], name, StringComparison.Ordinal))
            return argv[i + 1];
    return null;
}
