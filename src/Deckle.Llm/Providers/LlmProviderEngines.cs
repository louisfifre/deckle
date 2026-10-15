using Deckle.Security;

namespace Deckle.Llm;

// ─── Provider → engine ───────────────────────────────────────────────────────
//
// Builds the engine a provider calls for and keeps it while the provider's
// protocol and address stay the same, so a hosted client's connection pool is
// reused across requests. The API key is not part of the cache key: an engine
// reads it from the vault on each request, so a key saved in Settings takes
// effect on the next rewrite without a restart.
public sealed class LlmProviderEngines : IGenerationEngineResolver
{
    private readonly Func<IReadOnlyList<LlmProvider>> _providers;
    private readonly ISecretVault _vault;
    private readonly object _lock = new();
    private readonly Dictionary<string, (LlmProviderKind Kind, string Endpoint, IGenerationEngine Engine)> _cache = new();

    public LlmProviderEngines(Func<IReadOnlyList<LlmProvider>> providers, ISecretVault vault)
    {
        _providers = providers;
        _vault = vault;
    }

    /// <summary>The production resolver over the default vault. Cheap: the
    /// vault file is not touched until a key is read.</summary>
    public static LlmProviderEngines CreateDefault(Func<IReadOnlyList<LlmProvider>> providers) =>
        new(providers, SecretVault.CreateDefault());

    public IGenerationEngine? Resolve(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;

        LlmProvider? provider = null;
        foreach (var p in _providers())
        {
            if (p.Id == providerId) { provider = p; break; }
        }
        if (provider is null || !provider.Enabled) return null;

        string endpoint = provider.Endpoint?.Trim() ?? "";
        lock (_lock)
        {
            if (_cache.TryGetValue(providerId, out var cached)
                && cached.Kind == provider.Kind
                && cached.Endpoint == endpoint)
            {
                return cached.Engine;
            }

            var engine = Build(provider, endpoint);
            _cache[providerId] = (provider.Kind, endpoint, engine);
            return engine;
        }
    }

    private IGenerationEngine Build(LlmProvider provider, string endpoint)
    {
        string secretName = LlmProviderSecrets.ApiKeyName(provider.Id);
        string? ApiKey() => _vault.TryGet(secretName, out string? key) ? key : null;

        return provider.Kind switch
        {
            LlmProviderKind.Ollama           => new OllamaEngine(endpoint),
            LlmProviderKind.OpenAiCompatible => new OpenAiCompatibleEngine(endpoint, ApiKey),
            LlmProviderKind.Claude           => new ClaudeEngine(ApiKey),
            _ => throw new NotSupportedException($"Provider kind {provider.Kind} has no engine."),
        };
    }
}
