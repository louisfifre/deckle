using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Transcription.Tests;

// The step plan of a take (specs/deckle-dictation-rewriting.md), with the
// example settings of the spec: Affinage first, Lissage from 4,000 streaming,
// Arrangement from 12,000 at Stop.
[Trait("Category", "unit")]
public sealed class RewriteProfileSelectionTests
{
    private static LlmSettings Settings(bool startAt = false, bool firstStreams = true)
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
            Steps =
            [
                new RewriteStep { ProfileId = "aff", FromChars = 1000, Streaming = firstStreams },
                new RewriteStep { ProfileId = "lis", FromChars = 4000, Streaming = true },
                new RewriteStep { ProfileId = "arr", FromChars = 12000, Streaming = false },
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

    // Panel 4: past a step "at Stop", the passages go on with the highest
    // streaming step reached.
    [Fact]
    public void PastAStepAtStopThePassagesGoOnWithTheStreamingStepBelow()
    {
        RewriteStepPlan plan = Plan(Settings());

        Assert.Equal("lis", plan.HighestStreaming(20000)?.Profile?.Id);
        Assert.False(plan.Highest(20000)!.Value.Streaming);
    }

    [Fact]
    public void BelowTheFirstStreamingStepNothingStreams()
    {
        RewriteStepPlan plan = Plan(Settings(firstStreams: false));

        Assert.Null(plan.HighestStreaming(3000));
        Assert.Equal("lis", plan.HighestStreaming(4000)?.Profile?.Id);
    }

    // Page 8: when transcription does not stream, every step rewrites at Stop.
    [Fact]
    public void WithoutStreamingTranscriptionEveryStepRewritesAtStop()
    {
        RewriteStepPlan plan = Plan(Settings(), streaming: false);

        Assert.False(plan.HasStreamingStep);
        Assert.All(plan.Steps, step => Assert.False(step.Streaming));
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
        Assert.False(plan.HasStreamingStep);
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
