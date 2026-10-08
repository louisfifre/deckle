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
        RewriteProfile legacy = settings.Profiles[2];
        settings.ActiveProfileId = active.Id;
        settings.ActiveProfileName = active.Name;
        settings.PrimaryRewriteProfileId = legacy.Id;
        settings.PrimaryRewriteProfileName = legacy.Name;

        LlmSettingsMigrations.RepairProfileReferences(settings);

        Assert.Equal(active.Id, settings.ActiveProfileId);
        Assert.Equal(active.Name, settings.ActiveProfileName);
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
