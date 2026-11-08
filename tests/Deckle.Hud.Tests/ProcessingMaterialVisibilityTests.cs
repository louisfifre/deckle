using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

// Which HUD states show the processing material, on the outline and in the
// clock digits. With animations reduced, a turning material stood frozen
// mid-turn and read as broken: Transcribing and Rewriting then show only
// the stopped clock, while Recording keeps the outline that answers the
// microphone (ADR-0009).
[Trait("Category", "unit")]
public sealed class ProcessingMaterialVisibilityTests
{
    [Theory]
    [InlineData(ProcessingVariant.Transcribing)]
    [InlineData(ProcessingVariant.Rewriting)]
    public void WithAnimationsReduced_ProcessingStatesShowNoMaterial(ProcessingVariant variant)
    {
        Assert.False(HudComposition.ProcessingStroke.ShowsMaterial(variant, animationsEnabled: false));
    }

    [Fact]
    public void WithAnimationsReduced_TheRecordingOutlineStillShows()
    {
        Assert.True(HudComposition.ProcessingStroke.ShowsMaterial(
            ProcessingVariant.Recording, animationsEnabled: false));
    }

    [Theory]
    [InlineData(ProcessingVariant.Recording)]
    [InlineData(ProcessingVariant.Transcribing)]
    [InlineData(ProcessingVariant.Rewriting)]
    public void WithAnimationsOn_EveryStateShowsItsMaterial(ProcessingVariant variant)
    {
        Assert.True(HudComposition.ProcessingStroke.ShowsMaterial(variant, animationsEnabled: true));
    }
}
