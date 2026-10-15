using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class LlmSettingsMigrationsTests
{
    [Fact]
    public void LegacyPrimarySlotBecomesTheActiveProfile()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        RewriteProfile profile = settings.Profiles[1];
        settings.PrimaryRewriteProfileId = profile.Id;
        settings.PrimaryRewriteProfileName = profile.Name;

        bool mutated = LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(profile.Id, settings.ActiveProfileId);
        Assert.Equal(profile.Name, settings.ActiveProfileName);
        Assert.Null(settings.PrimaryRewriteProfileId);
        Assert.Null(settings.PrimaryRewriteProfileName);
    }

    [Fact]
    public void ExistingActiveProfileIsNotOverwrittenByTheLegacySlot()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        RewriteProfile active = settings.Profiles[0];
        RewriteProfile legacy = settings.Profiles[1];
        settings.ActiveProfileId = active.Id;
        settings.ActiveProfileName = active.Name;
        settings.PrimaryRewriteProfileId = legacy.Id;
        settings.PrimaryRewriteProfileName = legacy.Name;

        LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.Equal(active.Id, settings.ActiveProfileId);
        Assert.Equal(active.Name, settings.ActiveProfileName);
    }

    [Fact]
    public void LegacyFileGetsTheDefaultProvidersAndKeepsItsOllamaEndpoint()
    {
        var settings = new LlmSettings
        {
            Providers = [],
            OllamaEndpoint = "http://gpu-box:11434/api/generate",
        };
        foreach (var p in settings.Profiles) p.ProviderId = "";

        bool mutated = LlmSettingsMigrations.RepairProviders(settings);

        Assert.True(mutated);
        Deckle.Llm.LlmProvider ollama = Assert.Single(settings.Providers, p => p.Kind == Deckle.Llm.LlmProviderKind.Ollama);
        Assert.Equal("http://gpu-box:11434/api/generate", ollama.Endpoint);
        Assert.All(settings.Profiles, p => Assert.Equal(ollama.Id, p.ProviderId));
    }

    [Fact]
    public void MigratedFileIsLeftAlone()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.Apply(settings);
        settings.Profiles[0].ProviderId = "claude";

        bool mutated = LlmSettingsMigrations.RepairProviders(settings);

        Assert.False(mutated);
        Assert.Equal("claude", settings.Profiles[0].ProviderId);
    }

    [Fact]
    public void RenamedProfileResyncsTheActiveProfileName()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        RewriteProfile profile = settings.Profiles[0];
        settings.ActiveProfileId = profile.Id;
        settings.ActiveProfileName = profile.Name;
        profile.Name = "Renamed";

        bool mutated = LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(profile.Id, settings.ActiveProfileId);
        Assert.Equal("Renamed", settings.ActiveProfileName);
    }
}
