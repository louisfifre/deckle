using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class LlmSettingsNormalizerTests
{
    [Fact]
    public void ProfilesWithoutAnIdGetADistinctStableOne()
    {
        LlmSettings settings = LlmSettingsFixtures.WithTwoProfiles();

        bool mutated = LlmSettingsNormalizer.RepairProfileReferences(settings);

        Assert.True(mutated);
        Assert.All(settings.Profiles, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Id)));
        Assert.Equal(settings.Profiles.Count, settings.Profiles.Select(profile => profile.Id).Distinct().Count());
        Assert.False(LlmSettingsNormalizer.RepairProfileReferences(settings));
    }
}
