using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Models;

namespace Deckle.Llm;

/// <summary>What a provider answered to the model listing: its model names,
/// and the exception when it did not answer. An empty list with no failure
/// is a provider that answered nothing, such as Claude without a key.</summary>
public readonly record struct ProviderListing(IReadOnlyList<string> Names, Exception? Failure)
{
    public static readonly ProviderListing Empty = new(Array.Empty<string>(), null);
}

// ─── The models a provider serves ────────────────────────────────────────────
//
// Administration, not generation: the Settings page asks each provider what
// it can serve so a profile's model box can suggest names instead of asking
// the user to type one from memory. Each kind has its own listing call. A
// provider that cannot answer (down, key refused) yields an empty list and
// the exception that says why: the box stays free text, and the page tells
// the person which provider is not answering, the way it does for Ollama.
public static class ProviderModelCatalog
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<ProviderListing> ListAsync(
        LlmProvider provider, string? apiKey, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> names = provider.Kind switch
            {
                LlmProviderKind.Ollama           => await OllamaAsync(provider.Endpoint, cancellationToken),
                LlmProviderKind.OpenAiCompatible => await ChatCompletionsAsync(provider.Endpoint, apiKey, cancellationToken),
                LlmProviderKind.Claude           => await ClaudeAsync(apiKey, cancellationToken),
                _ => Array.Empty<string>(),
            };
            return new ProviderListing(names, null);
        }
        catch (Exception ex)
        {
            // A cancelled listing (the page's own timeout) is a failure like
            // any other here: the box stays free text either way.
            DeckleLlmSource.Log.ProviderListModelsFailed(provider.Name, ex.GetType().Name, ex.Message);
            return new ProviderListing(Array.Empty<string>(), ex);
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

    // The Models API needs the key; without one the box stays free text and
    // the key caption in Settings already says so, so it is not a failure.
    static async Task<IReadOnlyList<string>> ClaudeAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return Array.Empty<string>();

        var client = new AnthropicClient { ApiKey = apiKey };
        try
        {
            var page = await client.Models.List(new ModelListParams(), ct);
            var names = new List<string>();
            foreach (var m in page.Items) names.Add(m.ID);
            return names;
        }
        catch (AnthropicApiException ex)
        {
            // Same seam shape as ClaudeEngine: a refusal is an
            // HttpRequestException carrying the status, so the page reads
            // "key rejected" from the same code as the rewrite failure card.
            throw new HttpRequestException(ex.Message, inner: ex, statusCode: ex.StatusCode);
        }
    }
}
