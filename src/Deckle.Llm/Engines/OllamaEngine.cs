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
// Transport only: the provider lookup, the hard cap, and the human-facing
// failure outcomes live above the seam in the consumer. This engine throws
// and lets the caller decide.

public sealed class OllamaEngine : IGenerationEngine
{
    private readonly string _endpoint;

    public OllamaEngine(string endpoint) => _endpoint = endpoint;

    // HttpClient's default 100 s timeout is too short for large rewrites (long
    // transcriptions, big context, CPU-only Ollama). The caller's
    // CancellationToken is the only deadline.
    static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(60);

    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
    {
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

        // /api/ps exists only to enrich the admitted Transcription detail.
        // Refuse the task itself when that detail is disabled: otherwise a
        // logging toggle would still buy a worker, HTTP traffic and JSON
        // parsing every minute while a rewrite is in flight.
        CancellationTokenSource? pollDone = null;
        Task? pollingTask = null;
        if (IsProbeDetailEnabled())
        {
            var probeCts = new CancellationTokenSource();
            pollDone = probeCts;
            pollingTask = Task.Run(
                () => PollOllamaWhileBusy(_endpoint, sw, probeCts.Token));
        }

        HttpResponseMessage response;
        try
        {
            response = _http.PostAsync(generateUrl, content, cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            if (pollDone is not null)
            {
                pollDone.Cancel();
                // Per-tick failures are already in the admitted detail; what
                // lands here escaped the probe's own catches (the timer, the
                // URL), and the rewrite must not fail for its sake.
                try { pollingTask!.GetAwaiter().GetResult(); }
                catch (Exception ex) { DeckleLlmSource.Log.PsProbeFaulted(ex.GetType().Name, ex.Message); }
                pollDone.Dispose();
            }
        }

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
    /// Admitted technical detail, not a visible incident: the terminal
    /// RewriteTimeout / RewriteUnavailable events carry the human outcome.
    /// </summary>
    static async Task PollOllamaWhileBusy(string endpoint, System.Diagnostics.Stopwatch requestElapsed, CancellationToken ct)
    {
        string psUrl = NormalizePsUrl(endpoint);
        using var timer = new PeriodicTimer(POLL_INTERVAL);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                // Admission is live: if the user disables details during a
                // long rewrite, stop before the next HTTP request.
                if (!IsProbeDetailEnabled()) return;

                try
                {
                    using var resp = await _http.GetAsync(psUrl, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        DeckleLlmSource.Log.PsProbeRejectedDetail((int)resp.StatusCode);
                        continue;
                    }

                    string body = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(body);
                    if (!doc.RootElement.TryGetProperty("models", out var modelsArr) ||
                        modelsArr.ValueKind != JsonValueKind.Array ||
                        modelsArr.GetArrayLength() == 0)
                    {
                        DeckleLlmSource.Log.PsProbeEmpty();
                        continue;
                    }

                    var first = modelsArr[0];
                    string name = first.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? "?"
                        : "?";
                    long sizeVram = first.TryGetProperty("size_vram", out var vramProp) && vramProp.ValueKind == JsonValueKind.Number
                        ? vramProp.GetInt64()
                        : 0;
                    double vramGb = sizeVram / 1e9;

                    // `expires_at` is Ollama's keep_alive countdown for the
                    // resident model, logged as unloads_in_s to keep it
                    // distinct from the caller's own deadline (cap_s). 0 when
                    // Ollama reports none or it has run out.
                    double unloadsInS = 0;
                    if (first.TryGetProperty("expires_at", out var exp) &&
                        exp.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(exp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expAt))
                    {
                        var rem = expAt.ToUniversalTime() - DateTime.UtcNow;
                        if (rem.TotalSeconds > 0)
                            unloadsInS = rem.TotalSeconds;
                    }

                    DeckleLlmSource.Log.PsProbeBusyDetail(
                        name, vramGb, unloadsInS,
                        requestElapsed.ElapsedMilliseconds,
                        GenerationDeadline.HardCap.TotalSeconds);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    DeckleLlmSource.Log.PsProbeErrorDetail(ex.GetType().Name, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on every rewrite that outlives no tick or ends between
            // two: Generate cancels pollDone as soon as the main request
            // settles. Nothing was cancelled from the user's point of view, so
            // nothing is logged; the rewrite milestones close the operation.
        }
    }

    private static bool IsProbeDetailEnabled()
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Transcription,
            DeckleLlmSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Heartbeat);

    static string NormalizePsUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        string trimmed = endpoint.TrimEnd('/');
        if (trimmed.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^"/api/generate".Length] + "/api/ps";
        if (trimmed.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^"/api/chat".Length] + "/api/ps";
        return trimmed + "/api/ps";
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
