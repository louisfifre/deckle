using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Llm;

// ─── Ollama Administration Service ──────────────────────────────────────────
//
// Wraps Ollama REST endpoints for viewing and managing installed models: list,
// show, delete, health-check.
//
// Separate from LlmService (which only does /api/chat for rewriting).
//
// Model creation (`ollama create`, GGUF import) is not wrapped: it happens from
// the user's shell through the native Ollama CLI, then the Models section
// refreshes so they appear.
//
// The base URL is derived from LlmSettings.OllamaEndpoint by stripping the path
// (/api/chat) to keep only the origin (http://localhost:11434).

public sealed class OllamaService
{
    // Generous timeout: DeleteModelAsync on a large model can take time even on
    // localhost. Fast calls use their own CancellationTokenSource for a shorter
    // timeout.
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    readonly Func<string> _getEndpoint;

    /// <param name="getEndpoint">
    /// Callback returning the current endpoint (e.g. "http://localhost:11434/api/chat").
    /// Called on each request to follow configuration changes.
    /// </param>
    public OllamaService(Func<string> getEndpoint)
    {
        _getEndpoint = getEndpoint;
    }

    // ── Health check ────────────────────────────────────────────────────────

    /// <summary>
    /// Checks that Ollama is reachable (short timeout). Opt-in retry through
    /// <paramref name="maxAttempts"/> > 1: covers the classic PC boot race
    /// where Deckle starts before Ollama has finished listening on 11434.
    /// Default = 1 to stay fast for UI uses (Settings page); engine warmup
    /// explicitly requests maxAttempts=3.
    /// </summary>
    /// <param name="maxAttempts">Number of attempts. >= 1.</param>
    /// <param name="retryDelay">Pause between attempts. Ignored if maxAttempts=1.</param>
    public async Task<bool> IsAvailableAsync(int maxAttempts = 1, TimeSpan? retryDelay = null)
    {
        if (maxAttempts < 1) maxAttempts = 1;
        TimeSpan delay = retryDelay ?? TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var resp = await _http.GetAsync($"{BaseUrl}/api/tags", cts.Token);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch
            {
                // Retry on exception (connection refused, timeout). No log per
                // attempt to avoid pollution; the caller logs the final result.
            }

            if (attempt < maxAttempts)
            {
                try { await Task.Delay(delay); }
                catch { /* shouldn't happen, no token */ }
            }
        }

        return false;
    }

    // ── Model listing ───────────────────────────────────────────────────────

    /// <summary>
    /// Lists all local models. The caller provides a CancellationToken to bound
    /// the request; the shared HttpClient has a 30 min timeout, unsuitable for
    /// fast calls like list/show. Without a token, the call can hang up to
    /// 30 min if Ollama is saturated (e.g. concurrent GPU benchmark).
    /// HTTP errors and cancellation propagate to the caller. JsonException is
    /// trapped locally and returns an empty list: compromised Ollama or a
    /// reverse proxy returning error HTML must not break the UI.
    /// </summary>
    public async Task<List<OllamaModel>> ListModelsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"{BaseUrl}/api/tags", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            var result = JsonSerializer.Deserialize<OllamaTagsResponse>(json, _jsonOpts);
            return result?.Models ?? new();
        }
        catch (JsonException ex)
        {
            string preview = json.Length > 200 ? json[..200] + "..." : json;
            DeckleLlmSource.Log.ListModelsInvalidJson();
            DeckleLlmSource.Log.ListModelsInvalidJsonDetail(ex.Message, preview);
            return new();
        }
    }

    // ── Model details ───────────────────────────────────────────────────────

    /// <summary>
    /// Shows details for a model (template, system, params). See
    /// ListModelsAsync for the CancellationToken note and fail-soft return on
    /// invalid JSON.
    /// </summary>
    public async Task<OllamaModelInfo?> ShowModelAsync(string name, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new OllamaModelRequest { Model = name }, _jsonOpts);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{BaseUrl}/api/show", content, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonSerializer.Deserialize<OllamaModelInfo>(json, _jsonOpts);
        }
        catch (JsonException ex)
        {
            string preview = json.Length > 200 ? json[..200] + "..." : json;
            DeckleLlmSource.Log.ShowModelInvalidJson();
            DeckleLlmSource.Log.ShowModelInvalidJsonDetail(ex.Message, name, preview);
            return null;
        }
    }

    // ── Model deletion ──────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a local model. See ListModelsAsync for the CancellationToken
    /// note: deletion can be slow on large models, so the caller provides a
    /// generous timeout (10-30 s).
    /// </summary>
    public async Task DeleteModelAsync(string name, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/delete")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new OllamaModelRequest { Model = name }, _jsonOpts),
                Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Internal ────────────────────────────────────────────────────────────

    // Default fallback when the user-configured endpoint cannot be parsed
    // or is rejected by validation. Matches the Ollama out-of-the-box bind.
    const string DefaultBaseUrl = "http://localhost:11434";

    // Schemes whose use is allowed for the Ollama endpoint. Anything else
    // (file://, ftp://, custom schemes) is rejected to avoid handing weird
    // URIs to HttpClient. Loopback validation is a soft warn (some users
    // legitimately run Ollama on a separate LAN machine).
    static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    // Ensure the "non-loopback host" warning fires only once per distinct
    // host so the log isn't spammed on every request.
    static string? _lastNonLoopbackHostWarned;

    string BaseUrl
    {
        get
        {
            string endpoint = _getEndpoint();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return DefaultBaseUrl;

            if (!AllowedSchemes.Contains(uri.Scheme))
            {
                DeckleLlmSource.Log.EndpointSchemeNotAllowed();
                DeckleLlmSource.Log.EndpointSchemeNotAllowedDetail(uri.Scheme, DefaultBaseUrl);
                return DefaultBaseUrl;
            }

            string host = uri.Host;
            bool isLoopback =
                uri.IsLoopback ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host == "127.0.0.1" || host == "::1";

            if (!isLoopback && _lastNonLoopbackHostWarned != host)
            {
                DeckleLlmSource.Log.EndpointNonLoopbackHost();
                DeckleLlmSource.Log.EndpointNonLoopbackHostDetail(host);
                _lastNonLoopbackHostWarned = host;
            }

            return $"{uri.Scheme}://{uri.Authority}";
        }
    }

    // Options for endpoints that serialize typed DTOs (list, show, delete).
    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

// Generic request with "model" field (current Ollama API).
public sealed class OllamaModelRequest
{
    public string Model { get; set; } = "";
}

public sealed class OllamaTagsResponse
{
    public List<OllamaModel>? Models { get; set; }
}

public sealed class OllamaModel
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string ModifiedAt { get; set; } = "";
}

public sealed class OllamaModelInfo
{
    public string Modelfile { get; set; } = "";
    public string Template { get; set; } = "";
    public string System { get; set; } = "";
    public Dictionary<string, string>? Parameters { get; set; }
}
