using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Anthropic.Exceptions;

namespace Deckle.Benchmark.ModelCompare;

// ─── Rows, pricing and plumbing shared by the three commands ─────────────────

// One generation: a benchmark call, or a production rewrite pulled from the
// corpus (source "deckle"). The raw text is not repeated here: judge and
// summary reload it from the corpus by id, so the corpus stays its only home.
sealed record GenerationRow(
    string Id,
    string Bucket,
    int WordsIn,
    int WordsOut,
    string Model,
    string Prompt,
    string Source,
    string? Text,
    int? PromptTokens,
    int? EvalTokens,
    long? TotalMs,
    double? CostUsd,
    string? Error,
    // The repeat index of a campaign run (1..n); null on the original one-shot
    // matrix, so the files the first study wrote still read.
    int? Iteration = null);

// One judge verdict on one generation. Judgement is the judge's JSON as
// parsed; JudgeRaw keeps the answer when it did not parse, so a run never
// loses a paid call to a formatting slip.
sealed record JudgementRow(
    string Id,
    string Model,
    string Prompt,
    string Source,
    string JudgeModel,
    Judgement? Judgement,
    string? JudgeRaw,
    int? JudgePromptTokens,
    int? JudgeEvalTokens,
    long? JudgeMs,
    double? JudgeCostUsd,
    string? Error,
    int? Iteration = null,
    // The batch this verdict came from when the judge read several rewrites
    // in one call: its tokens, time and cost are split evenly over the rows.
    string? Batch = null);

sealed record Finding(string? Raw, string? Rewrite, string? Note);

sealed record Judgement(
    List<Finding>? TermSubstitutions,
    List<Finding>? MeaningInversions,
    List<Finding>? HedgesDropped,
    List<Finding>? Omissions,
    List<Finding>? Additions,
    string? Verdict)
{
    // The fixed category order of the schema, used by every report.
    public IEnumerable<(string Category, List<Finding> Items)> Categories()
    {
        yield return ("term_substitutions", TermSubstitutions ?? []);
        yield return ("meaning_inversions", MeaningInversions ?? []);
        yield return ("hedges_dropped",     HedgesDropped ?? []);
        yield return ("omissions",          Omissions ?? []);
        yield return ("additions",          Additions ?? []);
    }
}

static class Pricing
{
    // USD per million tokens (input, output). A 2026-10 snapshot of the public
    // price list: check it against the Anthropic console before trusting a
    // cost column. Output tokens include thinking tokens, which are billed as
    // output.
    static readonly Dictionary<string, (double In, double Out)> PerMTok = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-fable-5-1"]  = (10.00, 50.00),
        ["claude-opus-5-5"]   = (4.00, 20.00),
        ["claude-sonnet-5-5"] = (2.00, 10.00),
        ["claude-haiku-5-5"]  = (0.10, 0.50),
    };

    // Null for an unknown model: an absent price must not read as a free call.
    public static double? Cost(string model, int inputTokens, int outputTokens) =>
        PerMTok.TryGetValue(model, out var p)
            ? (inputTokens * p.In + outputTokens * p.Out) / 1_000_000.0
            : null;

    // The snapshot as data, so an export carries the prices its costs used.
    public static Dictionary<string, object> Snapshot() =>
        PerMTok.ToDictionary(kv => kv.Key, kv => (object)new { input_per_mtok = kv.Value.In, output_per_mtok = kv.Value.Out });
}

static class Transient
{
    // Worth one more try: rate limits, server-side failures and the network.
    // A 4xx other than 429 is the request's fault and would fail the same way.
    // ClaudeEngine rethrows API errors as HttpRequestException; the judge
    // calls the SDK directly and sees AnthropicApiException.
    public static bool Is(Exception ex) => ex switch
    {
        HttpRequestException h => h.StatusCode is null || IsRetryable(h.StatusCode.Value),
        AnthropicApiException a => IsRetryable(a.StatusCode),
        AnthropicIOException => true,
        TaskCanceledException => true,
        _ => false,
    };

    static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    public static string Describe(Exception ex) => ex is HttpRequestException { StatusCode: { } s }
        ? $"{ex.GetType().Name} {(int)s}: {ex.Message}"
        : $"{ex.GetType().Name}: {ex.Message}";
}

static partial class Words
{
    // Whitespace-separated tokens, the same count for raw and rewrite so the
    // ratio compares like with like.
    public static int Count(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : WordRegex().Matches(text).Count;

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordRegex();
}

static class Json
{
    public static readonly JsonSerializerOptions Input = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static readonly JsonSerializerOptions Output = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // For the files a person or another session opens whole (the frozen
    // campaign, the dashboard data): indented, nulls kept so a missing value
    // reads as missing rather than as an absent key.
    public static readonly JsonSerializerOptions Pretty = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<T> ReadLines<T>(string path) => File.ReadLines(path)
        .Where(l => !string.IsNullOrWhiteSpace(l))
        .Select(l => JsonSerializer.Deserialize<T>(l, Input)!)
        .ToList();
}
