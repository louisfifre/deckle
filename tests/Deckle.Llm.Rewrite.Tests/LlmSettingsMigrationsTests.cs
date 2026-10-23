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

    // The headline contract (the class header): a repair never deletes a legacy
    // rule and never clears the active profile, even when nothing it points at
    // exists any more.
    [Fact]
    public void OrphanReferencesAreLeftUntouchedAndNothingIsDeleted()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        settings.ActiveProfileId = "gone";
        settings.ActiveProfileName = "Deleted profile";
        settings.AutoRewriteRules = [new AutoRewriteRule { MinDurationSeconds = 30, ProfileId = "gone", ProfileName = "Deleted profile" }];
        settings.AutoRewriteRulesByWords = [new AutoRewriteRuleByWords { MinWordCount = 50, ProfileId = "gone", ProfileName = "Deleted profile" }];

        bool mutated = LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.False(mutated);
        Assert.Equal("gone", settings.ActiveProfileId);
        Assert.Equal("Deleted profile", settings.ActiveProfileName);
        AutoRewriteRule rule = Assert.Single(settings.AutoRewriteRules);
        Assert.Equal((30, "gone", "Deleted profile"), (rule.MinDurationSeconds, rule.ProfileId, rule.ProfileName));
        AutoRewriteRuleByWords byWords = Assert.Single(settings.AutoRewriteRulesByWords);
        Assert.Equal((50, "gone", "Deleted profile"), (byWords.MinWordCount, byWords.ProfileId, byWords.ProfileName));
    }

    [Fact]
    public void ProfilesWithoutAnIdGetADistinctStableOne()
    {
        var settings = new LlmSettings();
        foreach (RewriteProfile profile in settings.Profiles) profile.Id = "";

        bool mutated = LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.All(settings.Profiles, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Id)));
        Assert.Equal(settings.Profiles.Count, settings.Profiles.Select(profile => profile.Id).Distinct().Count());
        Assert.False(LlmSettingsMigrations.RepairProfileReferences(settings));
    }

    [Fact]
    public void ARuleKnownOnlyByNameIsPairedWithTheProfileIdIgnoringCase()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        RewriteProfile profile = settings.Profiles[0];
        settings.AutoRewriteRules = [new AutoRewriteRule { ProfileId = "", ProfileName = profile.Name.ToUpperInvariant() }];

        bool mutated = LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(profile.Id, settings.AutoRewriteRules[0].ProfileId);
    }

    [Fact]
    public void TheIdWinsOverAStaleNameOnARule()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        RewriteProfile first = settings.Profiles[0];
        RewriteProfile second = settings.Profiles[1];
        settings.AutoRewriteRulesByWords = [new AutoRewriteRuleByWords { ProfileId = first.Id, ProfileName = second.Name }];

        bool mutated = LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(first.Id, settings.AutoRewriteRulesByWords[0].ProfileId);
        Assert.Equal(first.Name, settings.AutoRewriteRulesByWords[0].ProfileName);
    }

    [Fact]
    public void AnEmptyActiveProfileStaysEmptyAndRepairIsIdempotent()
    {
        var settings = new LlmSettings();
        LlmSettingsMigrations.RepairProfileReferences(settings);
        settings.ActiveProfileId = null;
        settings.ActiveProfileName = null;

        Assert.False(LlmSettingsMigrations.RepairProfileReferences(settings));
        Assert.Null(settings.ActiveProfileId);
        Assert.Null(settings.ActiveProfileName);
    }}
