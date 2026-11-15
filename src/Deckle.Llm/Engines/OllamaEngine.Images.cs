using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Deckle.Llm;

// ─── Ollama engine, a request with an image ─────────────────────────────────
//
// Raw mode (OllamaEngine.cs) bypasses the model's template, and a vision
// model's template is where the picture finds its place in the prompt. So a
// request with an image goes through /api/chat, which applies the model's
// own template. The shape, from Ollama's API documentation (docs/api.md,
// "Chat request (with images)", read 2026-11-15):
//
//   { "model": "...", "stream": false,
//     "messages": [ { "role": "system", "content": "..." },
//                   { "role": "user", "content": "...", "images": ["<base64>"] } ] }
//
// and the answer, with stream false, is one object whose message.content is
// the text, with the same *_duration fields (nanoseconds) as /api/generate.
//
// The address is checked before the body is built: only a server on this
// machine receives a picture (LocalEndpoint).
public sealed partial class OllamaEngine
{
    private readonly HttpClient _imageHttp;

    private GenerationResult GenerateWithImage(GenerationRequest request, CancellationToken cancellationToken)
    {
        string url = NormalizeChatUrl(_endpoint);
        LocalEndpoint.RequireForImage(request, url);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        DeckleLlmSource.Log.RewriteStartedDetail(request.UserText.Length, request.Model, request.Label, "ollama-chat-image", "defaults");

        var messages = new List<object>(2);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = request.UserText, images = new[] { request.Image!.ToBase64() } });

        var options = new Dictionary<string, object>();
        if (request.Temperature.HasValue) options["temperature"] = request.Temperature.Value;
        if (request.NumCtx.HasValue)      options["num_ctx"]     = request.NumCtx.Value;

        var body = new
        {
            model      = request.Model,
            messages,
            stream     = false,
            keep_alive = request.KeepAlive,
            options    = options.Count == 0 ? null : options,
        };

        string json = JsonSerializer.Serialize(body, _jsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = _imageHttp.PostAsync(url, content, cancellationToken).GetAwaiter().GetResult();
        ImageRequestTransport.ThrowIfFailed(response, cancellationToken);

        string responseJson = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(responseJson);
        string text = doc.RootElement.TryGetProperty("message", out var message)
                      && message.TryGetProperty("content", out var contentProp)
                      && contentProp.ValueKind == JsonValueKind.String
            ? contentProp.GetString() ?? ""
            : "";

        sw.Stop();
        string trimmed = text.Trim();
        var m = ExtractMetrics(doc.RootElement);
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

    /// <summary>The server root, its /api/generate or its /api/chat: all
    /// land on /api/chat.</summary>
    internal static string NormalizeChatUrl(string endpoint)
    {
        string trimmed = (endpoint ?? "").Trim().TrimEnd('/');
        if (trimmed.Length == 0) return trimmed;
        if (trimmed.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/api/generate".Length];
        else if (trimmed.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/api/chat".Length];
        return trimmed + "/api/chat";
    }
}
