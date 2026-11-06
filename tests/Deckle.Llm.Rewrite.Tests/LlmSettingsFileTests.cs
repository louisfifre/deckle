using System.Text.Json;
using Deckle.Core;
using Deckle.Llm;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// LlmSettings through the real store and the real migration hook, from a file
// on disk: the load, the save and the reload a person's settings.json goes
// through. The options mirror LlmSettingsService's.
public sealed class LlmSettingsFileTests : IDisposable
{
    private const string CustomEndpoint = "http://gpu-box:11434/api/generate";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"deckle-llm-test-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_folder, "settings.json");

    public LlmSettingsFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private JsonSettingsStore<LlmSettings> OpenStore(string? initialText = null)
    {
        if (initialText is not null) File.WriteAllText(FilePath, initialText);
        return new JsonSettingsStore<LlmSettings>(
            FilePath, $"Deckle-Llm-Test-{Guid.NewGuid():N}", Options,
            postLoadMigration: LlmSettingsMigrations.Apply);
    }

    // A file from the Ollama-only era: one endpoint, profiles without a
    // provider, and no providers list at all.
    private const string OllamaOnlyEraFile = """
        {
          "enabled": true,
          "ollamaEndpoint": "http://gpu-box:11434/api/generate",
          "profiles": [ { "name": "Old", "model": "llama3", "systemPrompt": "Rewrite." } ]
        }
        """;

    [Fact]
    [Trait("Category", "regression")]
    public void CustomOllamaEndpointSurvivesTheMigrationOfAnOllamaOnlyFile()
    {
        using var store = OpenStore(OllamaOnlyEraFile);

        LlmProvider ollama = Assert.Single(store.Current.Providers, p => p.Kind == LlmProviderKind.Ollama);
        Assert.Equal(CustomEndpoint, ollama.Endpoint);
        Assert.All(store.Current.Profiles, p => Assert.Equal(ollama.Id, p.ProviderId));

        store.Flush();
        store.Reload();

        ollama = Assert.Single(store.Current.Providers, p => p.Kind == LlmProviderKind.Ollama);
        Assert.Equal(CustomEndpoint, ollama.Endpoint);
    }
}
