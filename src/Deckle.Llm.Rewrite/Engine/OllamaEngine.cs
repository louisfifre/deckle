using System.Diagnostics.Tracing;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Diagnostics;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Ollama engine (RAW mode) ────────────────────────────────────────────────
//
// Calls /api/generate with raw=true and a client-side pre-formatted prompt.
// Completely bypasses Ollama's TEMPLATE system because models imported from
// HuggingFace as GGUF often come with a generic Modelfile
// (TEMPLATE {{ .Prompt }}), which silently breaks the input format expected
// by the model — typical symptom: the model produces gibberish, echoes or
// loops.
//
// The template is determined client-side from the model name (family
// Mistral/Llama/Qwen/Gemma/Phi/ChatML), applied manually, and sent via
// raw=true — Ollama doesn't touch the prompt.
//
// Transport only: the profile lookup, the hard cap, and the human-facing
// failure outcomes (timeout overlay, unavailable overlay) live above the
// seam in RewriteService. This engine throws and lets the caller decide.

public sealed class OllamaEngine : IRewriteEngine
{
    // Default HttpClient.Timeout is 100 s — too short for large rewrites
    // (long transcriptions, big context, CPU-only Ollama). We disable the
    // built-in timeout; the caller's CancellationToken is the only deadline.
    static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    // /api/ps probe cadence while waiting for /api/generate to return.
    static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(60);

    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RewriteResult Generate(RewriteEngineRequest request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (prompt, stops, family) = PromptTemplates.Build(request.Model, request.SystemPrompt, request.UserText);
        string generateUrl = NormalizeGenerateUrl(request.Endpoint);
        var options = BuildOptions(request, stops);

        DeckleLlmSource.Log.RewriteStarted();
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
                () => PollOllamaWhileBusy(request.Endpoint, sw, probeCts.Token));
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
                try { pollingTask!.GetAwaiter().GetResult(); }
                catch { /* Probe failures are already present in admitted detail. */ }
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
            DeckleLlmSource.Log.RewriteCompleted();
            DeckleLlmSource.Log.RewriteCompletedDetail(sw.ElapsedMilliseconds, request.UserText.Length, trimmed.Length, request.Label);
            DeckleLlmSource.Log.RewriteMetrics(FormatMetrics(doc.RootElement));

            // Pull the same Ollama metrics the Verbose line above shows
            // and lift them up to the caller in ms/tokens. Two lookups of
            // each field would duplicate the JSON parse cost — done in
            // ExtractMetrics in one pass.
            var m = ExtractMetrics(doc.RootElement);
            return new RewriteResult(
                Text:         trimmed,
                TotalMs:      sw.ElapsedMilliseconds,
                OllamaLoadMs: m.LoadMs,
                PromptEvalMs: m.PromptEvalMs,
                EvalMs:       m.EvalMs,
                PromptTokens: m.PromptTokens,
                EvalTokens:   m.EvalTokens);
        }
    }

    /// <summary>
    /// Periodically probes Ollama's /api/ps while a /api/generate call is in
    /// flight. This is admitted technical detail, not a visible incident: the
    /// terminal RewriteTimeout / RewriteUnavailable events already carry the
    /// human outcome. Stops cleanly when the request settles or admission is
    /// disabled while it is running.
    /// </summary>
    static async Task PollOllamaWhileBusy(string endpoint, System.Diagnostics.Stopwatch requestElapsed, CancellationToken ct)
    {
        string psUrl = NormalizePsUrl(endpoint);
        using var timer = new PeriodicTimer(POLL_INTERVAL);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                // Admission is live. If the user disables details during a
                // long rewrite, stop before the next HTTP request and parse.
                if (!IsProbeDetailEnabled()) return;

                try
                {
                    using var resp = await _http.GetAsync(psUrl, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        DeckleLlmSource.Log.PsProbeUnreachableDetail((int)resp.StatusCode);
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
                    // resident model. Rendered as "unloads in Xs" to keep it
                    // distinct from the caller's own deadline — both are
                    // durations but they mean different things.
                    string unloadSuffix = "";
                    if (first.TryGetProperty("expires_at", out var exp) &&
                        exp.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(exp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expAt))
                    {
                        var rem = expAt.ToUniversalTime() - DateTime.UtcNow;
                        if (rem.TotalSeconds > 0)
                            unloadSuffix = $", unloads in {rem.TotalSeconds:F0}s";
                    }

                    double waitedSeconds = requestElapsed.Elapsed.TotalSeconds;
                    double capMinutes    = RewriteService.REWRITE_HARD_CAP.TotalMinutes;
                    DeckleLlmSource.Log.OllamaBusyDetail(name, vramGb, unloadSuffix, waitedSeconds, capMinutes);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    DeckleLlmSource.Log.PsProbeFailedDetail(ex.GetType().Name, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: Generate cancels pollDone as soon as the main request
            // settles. Cross-cutting Cancellation sub-provider: expected
            // upstream propagation from the finally (`pollDone.Cancel()`).
            // age_ms reflects elapsed request time when polling stops.
            DeckleCancellationSource.Log.OperationCancelled(
                "llm-warmup", "upstream", (int)requestElapsed.ElapsedMilliseconds);
        }
    }

    private static bool IsProbeDetailEnabled()
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Transcription,
            DeckleLlmSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Heartbeat);

    /// <summary>
    /// Derives the /api/ps URL from a /api/generate or /api/chat endpoint. If
    /// the endpoint shape is unknown, treat it as the base and append /api/ps.
    /// </summary>
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
    /// Converts a configured endpoint (which may historically point to
    /// /api/chat or already to /api/generate) to /api/generate. Any other
    /// form is left as-is — the user may have an exotic reverse proxy.
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

    /// <summary>
    /// Human-readable format of generation options sent to Ollama. Returns a
    /// string like "temp=0.15 ctx=32768 top_p=0.90 rep=1.10". Only non-null
    /// options are displayed.
    /// </summary>
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

    /// <summary>
    /// Extracts and formats metrics returned by Ollama in the /api/generate
    /// response. Same semantics as /api/chat (fields in nanoseconds).
    ///   - total_duration    : total server time
    ///   - load_duration     : model load time (0 if already warm)
    ///   - prompt_eval_count : prompt token count (input)
    ///   - prompt_eval_duration : prompt evaluation time
    ///   - eval_count        : generated token count (output)
    ///   - eval_duration     : generation time (useful for tok/s)
    /// </summary>
    static string FormatMetrics(JsonElement root)
    {
        long total = GetLong(root, "total_duration");
        long load  = GetLong(root, "load_duration");
        long pCnt  = GetLong(root, "prompt_eval_count");
        long pDur  = GetLong(root, "prompt_eval_duration");
        long eCnt  = GetLong(root, "eval_count");
        long eDur  = GetLong(root, "eval_duration");

        double totalMs = total / 1e6;
        double loadMs  = load  / 1e6;
        double pMs     = pDur  / 1e6;
        double eMs     = eDur  / 1e6;

        double pTokPerSec = pDur > 0 ? pCnt * 1e9 / pDur : 0;
        double eTokPerSec = eDur > 0 ? eCnt * 1e9 / eDur : 0;

        return $"metrics: total={totalMs:F0}ms load={loadMs:F0}ms | "
             + $"prompt {pCnt}tok en {pMs:F0}ms ({pTokPerSec:F1} tok/s) | "
             + $"output {eCnt}tok en {eMs:F0}ms ({eTokPerSec:F1} tok/s)";
    }

    static long GetLong(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt64();
        return 0;
    }

    // Same Ollama fields as FormatMetrics, extracted in ms / token counts so
    // the caller can stash them in a structured payload (LatencyPayload).
    // Kept as a private struct to avoid leaking the internal field names.
    // Nanoseconds → milliseconds: integer division to stay aligned with the
    // ms-int convention of the logging inventory (no need for sub-ms precision
    // on durations measured server-side over hundreds of ms).
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
    /// Builds the generation options dictionary from the request's nullable
    /// fields. Family stops are always added to prevent the model from
    /// continuing past its end-of-turn token.
    /// </summary>
    static Dictionary<string, object>? BuildOptions(RewriteEngineRequest request, string[] stops)
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
