using System;
using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

// How each microphone level sample reaches the recording outline. The
// Windows "Animation effects" preference only takes the glide away: the
// outline once stopped answering the microphone altogether when it was off
// (RecordingOutlineBugNotes.md beside this file).
[Trait("Category", "unit")]
public sealed class RecordingLevelStepTests
{
    [Theory]
    [Trait("Category", "regression")]
    [InlineData(0f)]
    [InlineData(0.4f)]
    [InlineData(1f)]
    public void TheOutlineFollowsTheMicrophoneWithAnimationEffectsOff(float level)
    {
        var step = HudComposition.ProcessingStroke.LevelStep(level, animationsEnabled: false);

        Assert.Equal(level, step.Opacity);
        Assert.Equal(TimeSpan.Zero, step.Glide);
    }

    [Fact]
    public void WithAnimationEffectsOn_EachSampleGlidesOverOneCaptureFrame()
    {
        var step = HudComposition.ProcessingStroke.LevelStep(0.4f, animationsEnabled: true);

        Assert.Equal(0.4f, step.Opacity);
        Assert.Equal(TimeSpan.FromMilliseconds(50), step.Glide);
    }

    [Theory]
    [InlineData(-0.2f, 0f)]
    [InlineData(1.3f, 1f)]
    public void ALevelOutsideTheUnitRangeIsClamped(float level, float expected)
    {
        Assert.Equal(expected, HudComposition.ProcessingStroke.LevelStep(level, animationsEnabled: false).Opacity);
        Assert.Equal(expected, HudComposition.ProcessingStroke.LevelStep(level, animationsEnabled: true).Opacity);
    }
}
