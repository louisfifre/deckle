using System.Diagnostics.Tracing;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Diagnostics;

namespace Deckle.Llm;

// ─── Ollama engine (RAW mode) ────────────────────────────────────────────────
//
// Calls /api/generate with raw=true and a prompt PromptTemplates formats
// client-side. This bypasses Ollama's TEMPLATE system because models imported
// from HuggingFace as GGUF often come with a generic Modelfile
// (TEMPLATE {{ .Prompt }}), which silently breaks the input format the model
// expects: it then produces gibberish, echoes or loops. A model with a sound
// template is better served through Ollama's /v1 by OpenAiCompatibleEngine.
//
// A reasoning level is dropped on purpose. Ollama's `think` switches a
// model's reasoning through the chat template, and raw mode is precisely the
// path that bypasses the template; its docs (2026-11) say nothing of `think`
// with raw=true. The /v1 path drops it as well (ReasoningLevel's header).
//
// Transport only: the provider lookup, the hard cap, and the human-facing
// failure outcomes live above the seam in the consumer. This engine throws
// and lets the caller decide.

public sealed partial class OllamaEngine : IGenerationEngine
{
    private readonly string _endpoint;

    public OllamaEngine(string endpoint) : this(endpoint, ImageRequestTransport.Client) { }

    // The tests hand in a client whose handler records what would be sent.
    internal OllamaEngine(string endpoint, HttpClient imageHttp)
    {
        _endpoint = endpoint;
        _imageHttp = imageHttp;
    }

    // HttpClient's default 100 s timeout is too short for large rewrites (long
    // transcriptions, big context, CPU-only Ollama). The caller's
    // CancellationToken is the only deadline.
    static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
    {
        // Raw mode formats the prompt client-side and has no place for a
        // picture; a request with one goes through /api/chat (OllamaEngine.Images.cs).
        if (request.Image is not null) return GenerateWithImage(request, cancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (prompt, stops, family) = PromptTemplates.Build(request.Model, request.SystemPrompt, request.UserText);
        string generateUrl = NormalizeGenerateUrl(_endpoint);
        var options = BuildOptions(request, stops);

        // RewriteService opens and closes the rewrite milestone; the engine
        // adds only what its transport sees.
        DeckleLlmSource.Log.RewriteStartedDetail(request.UserText.Length, request.Model, request.Label, family, FormatOptions(options));

        var body = new
        {
            model      = request.Model,
            prompt,
            raw        = true,
            stream     = false,
            keep_alive = request.KeepAlive,
            options
        };

        string json = JsonSerializer.Serialize(body, _jsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = _http.PostAsync(generateUrl, content, cancellationToken).GetAwaiter().GetResult();

        using (response)
        {
            response.EnsureSuccessStatusCode();

            string responseJson = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(responseJson);
            string? rewritten = doc.RootElement
                .GetProperty("response")
                .GetString();

            sw.Stop();
            string trimmed = PromptTemplates.StripStops(rewritten ?? "", family).Trim();

            var m = ExtractMetrics(doc.RootElement);
            // done_reason is "stop" or "length": a "length" answer was cut by
            // the token budget, which no other field shows.
            string stopReason = doc.RootElement.TryGetProperty("done_reason", out var dr) && dr.ValueKind == JsonValueKind.String
                ? dr.GetString() ?? ""
                : "";
            DeckleLlmSource.Log.RewriteResponseDetail(
                sw.ElapsedMilliseconds, request.UserText.Length, trimmed.Length, request.Label,
                m.LoadMs, m.PromptEvalMs, m.EvalMs, m.PromptTokens, m.EvalTokens, stopReason);

            return new GenerationResult(
                Text:         trimmed,
                TotalMs:      sw.ElapsedMilliseconds,
                LoadMs:       m.LoadMs,
                PromptEvalMs: m.PromptEvalMs,
                EvalMs:       m.EvalMs,
                PromptTokens: m.PromptTokens,
                EvalTokens:   m.EvalTokens);
        }
    }

    /// <summary>
    /// A stored endpoint may point to /api/chat. Any other form is left as-is:
    /// the user may run an exotic reverse proxy.
    /// </summary>
    static string NormalizeGenerateUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        if (endpoint.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            return endpoint[..^"/api/chat".Length] + "/api/generate";
        if (endpoint.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
            return endpoint;
        return endpoint;
    }

    static string FormatOptions(Dictionary<string, object>? opts)
    {
        if (opts is null || opts.Count == 0) return "defaults";
        var parts = new List<string>(opts.Count);
        foreach (var kv in opts)
        {
            if (kv.Key == "stop") continue; // noise in logs
            string key = kv.Key switch
            {
                "temperature"    => "temp",
                "num_ctx"        => "ctx",
                "top_p"          => "top_p",
                "repeat_penalty" => "rep",
                _                => kv.Key
            };
            parts.Add($"{key}={kv.Value}");
        }
        return parts.Count == 0 ? "defaults" : string.Join(" ", parts);
    }

    static long GetLong(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt64();
        return 0;
    }

    // Ollama reports every *_duration field in nanoseconds (load_duration is
    // 0 when the model is already warm). Nanoseconds to milliseconds by
    // integer division: the logging inventory records whole milliseconds.
    private readonly record struct OllamaMetrics(
        long LoadMs, long PromptEvalMs, long EvalMs, int PromptTokens, int EvalTokens);

    static OllamaMetrics ExtractMetrics(JsonElement root)
    {
        long load = GetLong(root, "load_duration");
        long pDur = GetLong(root, "prompt_eval_duration");
        long eDur = GetLong(root, "eval_duration");
        int  pCnt = (int)GetLong(root, "prompt_eval_count");
        int  eCnt = (int)GetLong(root, "eval_count");
        return new OllamaMetrics(
            LoadMs:       load / 1_000_000,
            PromptEvalMs: pDur / 1_000_000,
            EvalMs:       eDur / 1_000_000,
            PromptTokens: pCnt,
            EvalTokens:   eCnt);
    }

    /// <summary>
    /// Family stops are always sent so the model never continues past its
    /// end-of-turn token.
    /// </summary>
    static Dictionary<string, object>? BuildOptions(GenerationRequest request, string[] stops)
    {
        Dictionary<string, object>? opts = null;

        void Add(string key, object value)
        {
            opts ??= new();
            opts[key] = value;
        }

        if (request.Temperature.HasValue)   Add("temperature",    request.Temperature.Value);
        if (request.NumCtx.HasValue)        Add("num_ctx",        request.NumCtx.Value);
        if (request.TopP.HasValue)          Add("top_p",          request.TopP.Value);
        if (request.RepeatPenalty.HasValue) Add("repeat_penalty", request.RepeatPenalty.Value);

        if (stops.Length > 0) Add("stop", stops);

        return opts;
    }
}
