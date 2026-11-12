using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Transcription.Tests;

// The step plan of a take (specs/deckle-dictation-rewriting.md), with the
// example settings of the spec: Affinage first in blocks of 600, Lissage from
// 4,000 in blocks of 2,000, Arrangement from 12,000 in blocks of 6,000.
[Trait("Category", "unit")]
public sealed class RewriteProfileSelectionTests
{
    private static LlmSettings Settings(bool startAt = false, bool whileSpeaking = true)
    {
        var settings = new LlmSettings
        {
            Enabled = true,
            Profiles =
            [
                new RewriteProfile { Id = "aff", Name = "Affinage" },
                new RewriteProfile { Id = "lis", Name = "Lissage" },
                new RewriteProfile { Id = "arr", Name = "Arrangement" },
            ],
        };
        settings.Dictation = new DictationRewriteSettings
        {
            Enabled = true,
            StartAtLength = startAt,
            RewriteWhileSpeaking = whileSpeaking,
            Steps =
            [
                new RewriteStep { ProfileId = "aff", FromChars = 1000, BlockChars = 600 },
                new RewriteStep { ProfileId = "lis", FromChars = 4000, BlockChars = 2000 },
                new RewriteStep { ProfileId = "arr", FromChars = 12000, BlockChars = 6000 },
            ],
        };
        return settings;
    }

    private static RewriteStepPlan Plan(LlmSettings settings, string? forced = null, bool streaming = true) =>
        RewriteProfileSelection.ForTake(settings, forced, streaming);

    [Theory]
    [InlineData(10, "aff")]
    [InlineData(3999, "aff")]
    [InlineData(4000, "lis")]
    [InlineData(20000, "arr")]
    public void TheHighestStepReachedTakesTheDictation(int chars, string profileId)
    {
        Assert.Equal(profileId, Plan(Settings()).Highest(chars)?.Profile?.Id);
    }

    // Page 3: with "Start at" at 1,000, a dictation of 600 characters stays
    // as said.
    [Fact]
    public void WithStartAtADictationBelowTheFirstStepStaysAsSaid()
    {
        RewriteStepPlan plan = Plan(Settings(startAt: true));

        Assert.Null(plan.Highest(600));
        Assert.Equal("aff", plan.Highest(1500)?.Profile?.Id);
    }

    // Page 6 and Behaviours: each step carries its own block size, and the
    // text closes at the first step's before any step is reached.
    [Fact]
    public void EachStepCarriesItsBlockSize()
    {
        RewriteStepPlan plan = Plan(Settings());

        Assert.Equal([600, 2000, 6000], plan.Steps.Select(step => step.BlockChars));
        Assert.Equal(600, plan.FirstBlockChars);
        Assert.Equal(6000, plan.Highest(20000)?.BlockChars);
    }

    // Page 7: switching "Rewrite while I speak" changes no length.
    [Theory]
    [InlineData(3999, "aff")]
    [InlineData(4000, "lis")]
    [InlineData(12000, "arr")]
    public void TheSameStepTakesTheDictationInBothModes(int chars, string profileId)
    {
        Assert.Equal(profileId, Plan(Settings(whileSpeaking: true)).Highest(chars)?.Profile?.Id);
        Assert.Equal(profileId, Plan(Settings(whileSpeaking: false)).Highest(chars)?.Profile?.Id);
    }

    [Fact]
    public void WithRewriteWhileSpeakingOffNothingLeavesBeforeStop()
    {
        Assert.False(Plan(Settings(whileSpeaking: false)).RewritesWhileSpeaking);
        Assert.True(Plan(Settings(whileSpeaking: true)).RewritesWhileSpeaking);
    }

    // Page 3: when transcription does not stream, every step rewrites at Stop.
    [Fact]
    public void WithoutStreamingTranscriptionEveryStepRewritesAtStop()
    {
        RewriteStepPlan plan = Plan(Settings(), streaming: false);

        Assert.False(plan.RewritesWhileSpeaking);
        Assert.Equal("lis", plan.Highest(5000)?.Profile?.Id);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RewritingOrRewritingDictationsOffLeavesNoStep(bool rewriting, bool dictations)
    {
        LlmSettings settings = Settings();
        settings.Enabled = rewriting;
        settings.Dictation.Enabled = dictations;

        Assert.Same(RewriteStepPlan.None, Plan(settings));
    }

    // A profile's own shortcut asked for that rewrite: it holds even with
    // rewriting switched off, and the steps play no part.
    [Fact]
    public void AProfileShortcutRewritesWithItsProfileWhateverTheSteps()
    {
        LlmSettings settings = Settings();
        settings.Enabled = false;

        RewriteStepPlan plan = Plan(settings, forced: "arr");

        Assert.Equal("arr", plan.Forced?.Id);
        Assert.Empty(plan.Steps);
        Assert.False(plan.RewritesWhileSpeaking);
    }

    // A step whose profile was deleted is skipped: the step below it takes the
    // dictation, and the plan names the profile it missed.
    [Fact]
    public void AStepWhoseProfileWasDeletedIsSkippedForTheStepBelow()
    {
        LlmSettings settings = Settings();
        settings.Dictation.Steps[1].ProfileId = "gone";

        RewriteStepPlan plan = Plan(settings);

        Assert.Equal("aff", plan.Highest(5000)?.Profile?.Id);
        Assert.Equal("gone", plan.MissingProfileAbove(5000));
        Assert.Null(plan.MissingProfileAbove(3000));
        Assert.Null(plan.MissingProfileAbove(20000));
    }

    // Settings never normalized (no step) rewrite nothing rather than fail.
    [Fact]
    public void SettingsWithoutStepsLeaveNoStep()
    {
        var settings = new LlmSettings { Enabled = true };
        settings.Dictation.Enabled = true;

        Assert.Same(RewriteStepPlan.None, Plan(settings));
    }
}
