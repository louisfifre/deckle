using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class LlmSettingsNormalizerTests
{
    [Fact]
    public void RenamedProfileResyncsTheActiveProfileName()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.RepairProfileReferences(settings);
        RewriteProfile profile = settings.Profiles[0];
        settings.ActiveProfileId = profile.Id;
        settings.ActiveProfileName = profile.Name;
        profile.Name = "Renamed";

        bool mutated = LlmSettingsNormalizer.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(profile.Id, settings.ActiveProfileId);
        Assert.Equal("Renamed", settings.ActiveProfileName);
    }

    // The class header's contract: a repair never clears the active profile,
    // even when nothing it points at exists any more.
    [Fact]
    public void AnOrphanActiveProfileIsLeftUntouched()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.RepairProfileReferences(settings);
        settings.ActiveProfileId = "gone";
        settings.ActiveProfileName = "Deleted profile";

        bool mutated = LlmSettingsNormalizer.RepairProfileReferences(settings);

        Assert.False(mutated);
        Assert.Equal("gone", settings.ActiveProfileId);
        Assert.Equal("Deleted profile", settings.ActiveProfileName);
    }

    [Fact]
    public void ProfilesWithoutAnIdGetADistinctStableOne()
    {
        var settings = new LlmSettings();
        foreach (RewriteProfile profile in settings.Profiles) profile.Id = "";

        bool mutated = LlmSettingsNormalizer.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.All(settings.Profiles, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Id)));
        Assert.Equal(settings.Profiles.Count, settings.Profiles.Select(profile => profile.Id).Distinct().Count());
        Assert.False(LlmSettingsNormalizer.RepairProfileReferences(settings));
    }

    [Fact]
    public void AnActiveProfileKnownOnlyByNameIsPairedWithTheProfileIdIgnoringCase()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.RepairProfileReferences(settings);
        RewriteProfile profile = settings.Profiles[0];
        settings.ActiveProfileId = null;
        settings.ActiveProfileName = profile.Name.ToUpperInvariant();

        bool mutated = LlmSettingsNormalizer.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(profile.Id, settings.ActiveProfileId);
    }

    [Fact]
    public void TheIdWinsOverAStaleNameOnTheActiveProfile()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.RepairProfileReferences(settings);
        RewriteProfile first = settings.Profiles[0];
        RewriteProfile second = settings.Profiles[1];
        settings.ActiveProfileId = first.Id;
        settings.ActiveProfileName = second.Name;

        bool mutated = LlmSettingsNormalizer.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.Equal(first.Id, settings.ActiveProfileId);
        Assert.Equal(first.Name, settings.ActiveProfileName);
    }

    [Fact]
    public void AnEmptyActiveProfileStaysEmptyAndRepairIsIdempotent()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.RepairProfileReferences(settings);
        settings.ActiveProfileId = null;
        settings.ActiveProfileName = null;

        Assert.False(LlmSettingsNormalizer.RepairProfileReferences(settings));
        Assert.Null(settings.ActiveProfileId);
        Assert.Null(settings.ActiveProfileName);
    }
}
