using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Llm;

// ─── Service d'administration Ollama ─────────────────────────────────────────
//
// Wraps les endpoints REST d'Ollama pour la consultation et la gestion des
// modèles installés : list, show, delete, health-check.
//
// Séparé de LlmService (qui ne fait que du /api/chat pour la réécriture).
//
// La création de modèles (`ollama create`, import GGUF) n'est pas wrappée :
// elle se fait depuis le shell utilisateur via le CLI Ollama natif, puis la
// section Models se rafraîchit pour les voir apparaître.
//
// La base URL est dérivée de LlmSettings.OllamaEndpoint en strippant
// le path (/api/chat) pour ne garder que l'origin (http://localhost:11434).

public sealed class OllamaService
{
    // Timeout généreux : DeleteModelAsync sur un gros modèle peut prendre
    // du temps même en localhost. Les appels rapides utilisent leur propre
    // CancellationTokenSource pour un timeout plus court.
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    readonly Func<string> _getEndpoint;

    /// <param name="getEndpoint">
    /// Callback qui retourne l'endpoint courant (ex. "http://localhost:11434/api/chat").
    /// Appelé à chaque requête pour suivre les changements de config.
    /// </param>
    public OllamaService(Func<string> getEndpoint)
    {
        _getEndpoint = getEndpoint;
    }

    // ── Health check ────────────────────────────────────────────────────────

    /// <summary>
    /// Vérifie qu'Ollama est joignable (timeout court). Opt-in retry via
    /// <paramref name="maxAttempts"/> > 1 — couvre la race classique au boot
    /// du PC où Deckle démarre avant qu'Ollama ait fini d'écouter sur 11434.
    /// Default = 1 pour rester rapide sur les usages UI (Settings page),
    /// le warmup engine demande explicitement maxAttempts=3.
    /// </summary>
    /// <param name="maxAttempts">Nombre de tentatives. ≥ 1.</param>
    /// <param name="retryDelay">Pause entre tentatives. Ignoré si maxAttempts=1.</param>
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
                // Retry sur exception (connection refused, timeout). Pas de
                // log par essai pour ne pas polluer — le caller logge le
                // résultat final.
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
    /// Liste tous les modèles locaux. Le caller fournit un CancellationToken
    /// pour borner la requête — le HttpClient partagé a un timeout de 30 min,
    /// inadapté aux appels rapides comme list/show. Sans token, l'appel peut
    /// pendre jusqu'à 30 min si Ollama est saturé (ex. benchmark GPU
    /// concurrent).
    /// HTTP errors et cancellation propagent au caller. JsonException est
    /// trappée localement et retourne une liste vide — Ollama compromis ou
    /// reverse proxy qui renvoie du HTML d'erreur ne doit pas casser l'UI.
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
            DeckleLlmSource.Log.ListModelsInvalidJson(ex.Message, preview);
            return new();
        }
    }

    // ── Model details ───────────────────────────────────────────────────────

    /// <summary>
    /// Affiche les détails d'un modèle (template, system, params). Voir
    /// ListModelsAsync pour la note sur le CancellationToken et le retour
    /// fail-soft sur JSON invalide.
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
            DeckleLlmSource.Log.ShowModelInvalidJson(ex.Message, name, preview);
            return null;
        }
    }

    // ── Model deletion ──────────────────────────────────────────────────────

    /// <summary>
    /// Supprime un modèle local. Voir ListModelsAsync pour la note sur le
    /// CancellationToken — la suppression peut être lente sur gros modèles
    /// donc le caller fournit un timeout généreux (10-30 s).
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

    // ── Interne ─────────────────────────────────────────────────────────────

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
                DeckleLlmSource.Log.EndpointSchemeNotAllowed(uri.Scheme, DefaultBaseUrl);
                return DefaultBaseUrl;
            }

            string host = uri.Host;
            bool isLoopback =
                uri.IsLoopback ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host == "127.0.0.1" || host == "::1";

            if (!isLoopback && _lastNonLoopbackHostWarned != host)
            {
                DeckleLlmSource.Log.EndpointNonLoopbackHost(host);
                _lastNonLoopbackHostWarned = host;
            }

            return $"{uri.Scheme}://{uri.Authority}";
        }
    }

    // Options pour les endpoints qui sérialisent des DTOs typés (list, show, delete).
    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

// Requête générique avec champ "model" (API Ollama actuelle).
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
