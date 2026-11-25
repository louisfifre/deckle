using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class DictationStepsNormalizerTests
{
    private static LlmSettings WithSteps(params (int From, int BlockChars)[] steps)
    {
        LlmSettings settings = LlmSettingsFixtures.WithTwoProfiles();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.Steps = steps
            .Select(step => new RewriteStep
            {
                ProfileId = settings.Profiles[0].Id,
                FromChars = step.From,
                BlockChars = step.BlockChars,
            })
            .ToList();
        return settings;
    }

    [Fact]
    public void FreshSettingsHaveOneFirstStepWithoutAProfileAndRewritingDictationsOff()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);

        Assert.Empty(settings.Profiles);
        RewriteStep first = Assert.Single(settings.Dictation.Steps);
        Assert.Equal("", first.ProfileId);
        Assert.Equal(0, first.FromChars);
        Assert.False(settings.Dictation.Enabled);
        Assert.False(LlmSettingsNormalizer.Apply(settings));
    }

    // The first step stays first; the later ones sort.
    [Fact]
    public void TheLaterStepsSortAndTheFirstStaysFirst()
    {
        LlmSettings settings = WithSteps((0, 600), (4000, 2000), (1000, 600));

        Assert.True(LlmSettingsNormalizer.NormalizeDictation(settings));

        Assert.Equal([0, 1000, 4000], settings.Dictation.Steps.Select(step => step.FromChars));
        Assert.Equal([600, 600, 2000], settings.Dictation.Steps.Select(step => step.BlockChars));
    }

    // A later step at or below the first moves just above it rather than
    // being dropped.
    [Fact]
    public void ALaterStepBelowTheFirstMovesJustAboveIt()
    {
        LlmSettings settings = WithSteps((1000, 600), (800, 600));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal([1000, 1100], settings.Dictation.Steps.Select(step => step.FromChars));
    }

    [Fact]
    public void LengthsSnapToTheirGrainAndAStepPastTheMaximumIsDropped()
    {
        LlmSettings settings = WithSteps((40, 600), (1234, 600), (29600, 600), (29900, 600));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal([0, 1200, 30000], settings.Dictation.Steps.Select(step => step.FromChars));
    }

    // Page 8: a block of 6,000 is accepted as typed; below the minimum or
    // above the maximum a size comes back within the bounds, and a step
    // without one takes the default.
    [Fact]
    public void ABlockSizeIsKeptWithinItsBoundsAndSixThousandIsAccepted()
    {
        LlmSettings settings = WithSteps((1000, 50), (4000, 0), (12000, 6000), (20000, 99000));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal(
            [DictationRewriteSettings.MinBlockChars, DictationRewriteSettings.DefaultBlockChars, 6000, DictationRewriteSettings.MaxBlockChars],
            settings.Dictation.Steps.Select(step => step.BlockChars));
    }
}
