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

    private static readonly string[] LegacyKeys =
        ["ollamaEndpoint", "primaryRewriteProfileId", "primaryRewriteProfileName",
         "autoRewriteRules", "ruleMetric", "autoRewriteRulesByWords"];

    private string[] LegacyKeysOnDisk()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        return LegacyKeys.Where(k => doc.RootElement.TryGetProperty(k, out _)).ToArray();
    }

    [Fact]
    [Trait("Category", "unit")]
    public void LegacyFieldsAreReadToMigrateButNotWrittenBack()
    {
        const string oldFile = """
            {
              "ollamaEndpoint": "http://gpu-box:11434/api/generate",
              "primaryRewriteProfileId": "p1",
              "primaryRewriteProfileName": "Old",
              "profiles": [ { "id": "p1", "name": "Old", "model": "llama3", "systemPrompt": "Rewrite." } ]
            }
            """;
        using var store = OpenStore(oldFile);

        Assert.Equal("p1", store.Current.ActiveProfileId);
        Assert.Equal(CustomEndpoint,
            Assert.Single(store.Current.Providers, p => p.Kind == LlmProviderKind.Ollama).Endpoint);

        // The store rewrites a migrated file on its own; a later save changes nothing.
        Assert.Empty(LegacyKeysOnDisk());
        store.Flush();
        Assert.Empty(LegacyKeysOnDisk());
    }

    [Fact]
    [Trait("Category", "unit")]
    public void LegacyRulesOfAnOldFileRoundTripButANewFileCarriesNone()
    {
        using var fresh = OpenStore();
        Assert.Empty(LegacyKeysOnDisk());
        fresh.Dispose();

        File.WriteAllText(FilePath, """
            { "autoRewriteRules": [ { "minDurationSeconds": 30, "profileId": "gone", "profileName": "Gone" } ] }
            """);
        using var old = OpenStore();
        old.Flush();

        Assert.Equal(["autoRewriteRules"], LegacyKeysOnDisk());
        Assert.Single(old.Current.AutoRewriteRules!);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AFileSavedByTheNewVersionLoadsWithItsProviders()
    {
        using var first = OpenStore();
        first.Current.Providers.Find(p => p.Kind == LlmProviderKind.Ollama)!.Endpoint = CustomEndpoint;
        first.Flush();
        first.Dispose();

        using var second = OpenStore();

        Assert.Equal(CustomEndpoint,
            Assert.Single(second.Current.Providers, p => p.Kind == LlmProviderKind.Ollama).Endpoint);
        Assert.Equal(3, second.Current.Providers.Count);
        Assert.Empty(LegacyKeysOnDisk());
    }
}
