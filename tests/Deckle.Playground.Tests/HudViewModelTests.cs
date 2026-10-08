using Deckle.Playground;
using Xunit;

namespace Deckle.Playground.Tests;

[Trait("Category", "unit")]
public sealed class HudViewModelTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void RecordingTargetDoesNotShowTheGenericConicFadeSection()
    {
        var viewModel = new HudViewModel { CurrentTarget = HudTarget.Recording };

        var sections = viewModel.ActiveTuningSections;

        Assert.DoesNotContain(HudTuningSection.ConicFade, sections);
        Assert.DoesNotContain(HudTuningSection.HueRotation, sections);
        Assert.Contains(HudTuningSection.Recording, sections);
    }

    // The per-target map is the contract (a section is shown only where its
    // rows reach the preview), so the literal lists are the expectation.
    [Theory]
    [InlineData(HudTarget.Charging)]
    [InlineData(HudTarget.Recording,
        HudTuningSection.Geometry, HudTuningSection.Palette, HudTuningSection.Recording,
        HudTuningSection.AudioMapping, HudTuningSection.SimulatedRms)]
    [InlineData(HudTarget.Transcribing,
        HudTuningSection.Geometry, HudTuningSection.Palette, HudTuningSection.ConicFade,
        HudTuningSection.HueRotation, HudTuningSection.ArcRotation, HudTuningSection.Transcribing,
        HudTuningSection.ClonePlacement)]
    [InlineData(HudTarget.Rewriting,
        HudTuningSection.Geometry, HudTuningSection.Palette, HudTuningSection.ConicFade,
        HudTuningSection.HueRotation, HudTuningSection.ArcRotation, HudTuningSection.Rewriting,
        HudTuningSection.ClonePlacement)]
    [InlineData(HudTarget.Conic,
        HudTuningSection.Geometry, HudTuningSection.Palette, HudTuningSection.HueRotation)]
    [InlineData(HudTarget.ArcMask,
        HudTuningSection.Geometry, HudTuningSection.ConicFade, HudTuningSection.ArcRotation)]
    [InlineData(HudTarget.Combined,
        HudTuningSection.Geometry, HudTuningSection.Palette, HudTuningSection.ConicFade,
        HudTuningSection.HueRotation, HudTuningSection.ArcRotation)]
    [InlineData(HudTarget.ConicClone,
        HudTuningSection.ConicFade, HudTuningSection.ClonePlacement)]
    public void EachTargetShowsOnlyTheSectionsItsPreviewConsumes(
        HudTarget target, params HudTuningSection[] expected)
    {
        var viewModel = new HudViewModel { CurrentTarget = target };

        Assert.Equal(expected, viewModel.ActiveTuningSections);
    }
}
