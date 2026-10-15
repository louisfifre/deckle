using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Llm;

// ─── Chat-completions engine ─────────────────────────────────────────────────
//
// Speaks the chat-completions shape that OpenAI published and that nearly
// every server since has copied: LM Studio, llama.cpp's server, Ollama's /v1,
// vLLM, and the hosted APIs of Mistral, OpenAI, Groq or OpenRouter. One
// engine, bound to an address and an optional key, covers all of them. The
// system prompt travels as its own message, so the server applies the model's
// own chat template; nothing is formatted client-side (contrast OllamaEngine).
//
// Transport only: the provider lookup, the hard cap and the human-facing
// failure outcomes live above the seam in the consumer. This engine throws
// and lets the caller decide.

public sealed class OpenAiCompatibleEngine : IGenerationEngine
{
    // HttpClient's default 100 s timeout is too short for long rewrites on a
    // CPU-only local server. The caller's CancellationToken is the only
    // deadline.
    static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _endpoint;
    private readonly Func<string?> _apiKey;

    public OpenAiCompatibleEngine(string endpoint, Func<string?> apiKey)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
    }

    public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string url = NormalizeChatUrl(_endpoint);

        DeckleLlmSource.Log.RewriteStarted();
        DeckleLlmSource.Log.RewriteStartedDetail(
            request.UserText.Length, request.Model, request.Label, "chat-completions", FormatOptions(request));

        var messages = new List<object>(2);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = request.UserText });

        var body = new
        {
            model       = request.Model,
            messages,
            stream      = false,
            temperature = request.Temperature,
            top_p       = request.TopP,
        };

        string json = JsonSerializer.Serialize(body, _jsonOpts);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        string? apiKey = _apiKey();
        if (!string.IsNullOrEmpty(apiKey))
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = _http.SendAsync(httpRequest, cancellationToken).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            // The body usually says why (unknown model, bad key); the status
            // alone would send the user guessing.
            string error = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}: {Trim(error, 300)}");
        }

        string responseJson = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        string text = root.TryGetProperty("choices", out var choices)
                      && choices.ValueKind == JsonValueKind.Array
                      && choices.GetArrayLength() > 0
                      && choices[0].TryGetProperty("message", out var message)
                      && message.TryGetProperty("content", out var content)
            ? content.GetString() ?? ""
            : "";

        int promptTokens = 0, evalTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            promptTokens = GetInt(usage, "prompt_tokens");
            evalTokens   = GetInt(usage, "completion_tokens");
        }

        sw.Stop();
        string trimmed = text.Trim();
        DeckleLlmSource.Log.RewriteCompleted();
        DeckleLlmSource.Log.RewriteCompletedDetail(sw.ElapsedMilliseconds, request.UserText.Length, trimmed.Length, request.Label);
        DeckleLlmSource.Log.RewriteMetrics(
            $"metrics: total={sw.ElapsedMilliseconds}ms | prompt {promptTokens}tok | output {evalTokens}tok");

        return new GenerationResult(
            Text:         trimmed,
            TotalMs:      sw.ElapsedMilliseconds,
            LoadMs:       0,
            PromptEvalMs: 0,
            EvalMs:       sw.ElapsedMilliseconds,
            PromptTokens: promptTokens,
            EvalTokens:   evalTokens);
    }

    /// <summary>A stored endpoint may be the server root, its /v1 base, or
    /// the full chat URL; all three land on /v1/chat/completions.</summary>
    internal static string NormalizeChatUrl(string endpoint)
    {
        string trimmed = (endpoint ?? "").Trim().TrimEnd('/');
        if (trimmed.Length == 0) return trimmed;
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return trimmed + "/chat/completions";
        return trimmed + "/v1/chat/completions";
    }

    static string FormatOptions(GenerationRequest request)
    {
        var parts = new List<string>(2);
        if (request.Temperature.HasValue) parts.Add($"temp={request.Temperature.Value}");
        if (request.TopP.HasValue)        parts.Add($"top_p={request.TopP.Value}");
        return parts.Count == 0 ? "defaults" : string.Join(" ", parts);
    }

    static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;

    static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
