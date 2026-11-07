using System.Text.Json;
using Deckle.Core;
using Deckle.Llm;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// LlmSettings through the real store and the real normalizer hook, from a file
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
            postLoadMigration: LlmSettingsNormalizer.Apply);
    }

    private static readonly string[] RetiredKeys =
        ["ollamaEndpoint", "primaryRewriteProfileId", "primaryRewriteProfileName",
         "autoRewriteRules", "ruleMetric", "autoRewriteRulesByWords"];

    private string[] RetiredKeysOnDisk()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        return RetiredKeys.Where(k => doc.RootElement.TryGetProperty(k, out _)).ToArray();
    }

    // The settings are read as they are: a key the model no longer has is
    // ignored, and the next save does not write it back.
    [Fact]
    [Trait("Category", "integration")]
    public void AKeyTheModelNoLongerHasIsIgnoredAndNotWrittenBack()
    {
        const string withRetiredKeys = """
            {
              "ollamaEndpoint": "http://gpu-box:11434/api/generate",
              "primaryRewriteProfileId": "p1",
              "autoRewriteRules": [ { "minDurationSeconds": 30, "profileId": "p1", "profileName": "Old" } ],
              "profiles": [ { "id": "p1", "name": "Old", "providerId": "ollama", "model": "llama3", "systemPrompt": "Rewrite." } ]
            }
            """;
        using var store = OpenStore(withRetiredKeys);

        Assert.Equal("p1", Assert.Single(store.Current.Profiles).Id);
        Assert.Null(store.Current.ActiveProfileId);

        store.Replace(store.Current);
        store.Flush();
        Assert.Empty(RetiredKeysOnDisk());
    }
    [Fact]
    [Trait("Category", "integration")]
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
        Assert.Empty(RetiredKeysOnDisk());
    }
}
