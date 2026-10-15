using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Models;

namespace Deckle.Llm;

// ─── The models a provider serves ────────────────────────────────────────────
//
// Administration, not generation: the Settings page asks each provider what
// it can serve so a profile's model box can suggest names instead of asking
// the user to type one from memory. Each kind has its own listing call, and a
// provider that cannot answer (down, no key) yields an empty list rather than
// an exception; the caller shows nothing and lets the box stay free text.
public static class ProviderModelCatalog
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<IReadOnlyList<string>> ListAsync(
        LlmProvider provider, string? apiKey, CancellationToken cancellationToken)
    {
        try
        {
            return provider.Kind switch
            {
                LlmProviderKind.Ollama           => await OllamaAsync(provider.Endpoint, cancellationToken),
                LlmProviderKind.OpenAiCompatible => await ChatCompletionsAsync(provider.Endpoint, apiKey, cancellationToken),
                LlmProviderKind.Claude           => await ClaudeAsync(apiKey, cancellationToken),
                _ => Array.Empty<string>(),
            };
        }
        catch (Exception ex)
        {
            // A cancelled listing (the page's own timeout) is a failure like
            // any other here: the box stays free text either way.
            DeckleLlmSource.Log.ProviderListModelsFailed();
            DeckleLlmSource.Log.ProviderListModelsFailedDetail(provider.Name, ex.GetType().Name, ex.Message);
            return Array.Empty<string>();
        }
    }

    static async Task<IReadOnlyList<string>> OllamaAsync(string endpoint, CancellationToken ct)
    {
        var models = await new OllamaService(() => endpoint).ListModelsAsync(ct);
        var names = new List<string>(models.Count);
        foreach (var m in models) names.Add(m.Name);
        return names;
    }

    // GET /v1/models, the listing every chat-completions server exposes:
    // { "data": [ { "id": "..." }, ... ] }.
    static async Task<IReadOnlyList<string>> ChatCompletionsAsync(string endpoint, string? apiKey, CancellationToken ct)
    {
        string url = OpenAiCompatibleEngine.NormalizeUrl(endpoint, "models");
        if (url.Length == 0) return Array.Empty<string>();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    names.Add(id.GetString()!);
            }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // The Models API needs the key; without one the box stays free text.
    static async Task<IReadOnlyList<string>> ClaudeAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return Array.Empty<string>();

        var client = new AnthropicClient { ApiKey = apiKey };
        var page = await client.Models.List(new ModelListParams(), ct);
        var names = new List<string>();
        foreach (var m in page.Items) names.Add(m.ID);
        return names;
    }
}
