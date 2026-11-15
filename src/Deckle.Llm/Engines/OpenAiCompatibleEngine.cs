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

    // A request with an image travels on its own client: no redirect, no
    // proxy (ImageRequestTransport). The tests hand in one whose handler
    // records what would be sent.
    private readonly HttpClient _imageHttp;

    public OpenAiCompatibleEngine(string endpoint, Func<string?> apiKey)
        : this(endpoint, apiKey, ImageRequestTransport.Client) { }

    internal OpenAiCompatibleEngine(string endpoint, Func<string?> apiKey, HttpClient imageHttp)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _imageHttp = imageHttp;
    }

    public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
    {
        string url = NormalizeChatUrl(_endpoint);
        // The same engine serves LM Studio on this machine and a hosted API:
        // a picture goes only to the first (LocalEndpoint), checked before
        // the body is built.
        LocalEndpoint.RequireForImage(request, url);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // RewriteService opens and closes the rewrite milestone; the engine
        // adds only what its transport sees.
        DeckleLlmSource.Log.RewriteStartedDetail(
            request.UserText.Length, request.Model, request.Label, "chat-completions", FormatOptions(request));

        var messages = new List<object>(2);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = UserContent(request) });

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

        using var response = (request.Image is null ? _http : _imageHttp)
            .SendAsync(httpRequest, cancellationToken).GetAwaiter().GetResult();
        if (request.Image is not null)
            ImageRequestTransport.ThrowIfFailed(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The body usually says why (unknown model, bad key); the status
            // alone would send the user guessing. The status also rides on
            // the exception so the consumer can word a rejected key or an
            // unknown model without parsing the message.
            string error = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}: {Trim(error, 300)}",
                inner: null,
                statusCode: response.StatusCode);
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

        // finish_reason is "stop" or "length" (cut by the token budget),
        // sometimes "content_filter": the one field that says a short answer
        // was truncated rather than finished.
        string finishReason = choices.ValueKind == JsonValueKind.Array
                              && choices.GetArrayLength() > 0
                              && choices[0].TryGetProperty("finish_reason", out var finish)
                              && finish.ValueKind == JsonValueKind.String
            ? finish.GetString() ?? ""
            : "";

        int promptTokens = 0, evalTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            promptTokens = GetInt(usage, "prompt_tokens");
            evalTokens   = GetInt(usage, "completion_tokens");
        }

        sw.Stop();
        string trimmed = text.Trim();
        // The protocol reports no load or prompt-evaluation timing: those stay
        // 0, as in the GenerationResult below.
        DeckleLlmSource.Log.RewriteResponseDetail(
            sw.ElapsedMilliseconds, request.UserText.Length, trimmed.Length, request.Label,
            0, 0, sw.ElapsedMilliseconds, promptTokens, evalTokens, finishReason);

        return new GenerationResult(
            Text:         trimmed,
            TotalMs:      sw.ElapsedMilliseconds,
            LoadMs:       0,
            PromptEvalMs: 0,
            EvalMs:       sw.ElapsedMilliseconds,
            PromptTokens: promptTokens,
            EvalTokens:   evalTokens);
    }

    // A text request keeps the plain string content it always sent. A
    // request with an image sends the typed parts of the chat-completions
    // shape: a text part, then an image part whose url is a base64 data URI.
    // llama.cpp's server documents it (tools/server/README.md: "image_url.url
    // can be ... base64 (raw or URI-encoded via data:image/...;base64)"); LM
    // Studio lists image input on its OpenAI-compatible endpoint without
    // showing the shape, which is OpenAI's own. Read on 2026-11-15.
    static object UserContent(GenerationRequest request)
    {
        if (request.Image is null) return request.UserText;
        string dataUri = $"data:{request.Image.MediaType};base64,{request.Image.ToBase64()}";
        return new object[]
        {
            new { type = "text", text = request.UserText },
            new { type = "image_url", image_url = new { url = dataUri } },
        };
    }

    /// <summary>A stored endpoint may be the server root, its /v1 base, or
    /// the full chat URL; all three land on /v1/chat/completions.</summary>
    internal static string NormalizeChatUrl(string endpoint) => NormalizeUrl(endpoint, "chat/completions");

    /// <summary>Resolves <paramref name="path"/> under the /v1 base a stored
    /// endpoint names, whether it was given as the server root, the /v1 base
    /// or the full chat URL.</summary>
    internal static string NormalizeUrl(string endpoint, string path)
    {
        string trimmed = (endpoint ?? "").Trim().TrimEnd('/');
        if (trimmed.Length == 0) return trimmed;
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/chat/completions".Length];
        if (!trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed += "/v1";
        return trimmed + "/" + path;
    }

    // The request's reasoning level is dropped on purpose. Ollama's /v1 and
    // LM Studio (since 0.4.8) accept OpenAI's `reasoning_effort`, but which
    // values a local model honours, and whether it can stop thinking at all,
    // differs model by model (ReasoningLevel's header); the model's own
    // default is what runs.
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
