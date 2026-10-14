using Deckle.Composition;
using Deckle.Playground;
using Xunit;
using static Deckle.Composition.HudComposition;

namespace Deckle.Playground.Tests;

[Trait("Category", "unit")]
public sealed class HudViewModelTests
{
    // The shipping materials: Recording Grey, Transcribing Grey, Rewriting Colour.
    private static readonly ConicArcStrokeConfig Shipping = new();

    private static ConicArcStrokeConfig AllStatesOn(HudMaterial material) => Shipping with
    {
        RecordingMaterial    = material,
        TranscribingMaterial = material,
        RewritingMaterial    = material,
    };

    // Even on the Colour material, where a hue rotation would show elsewhere:
    // the Recording outline freezes both rotations.
    [Theory]
    [Trait("Category", "regression")]
    [InlineData(HudMaterial.Grey)]
    [InlineData(HudMaterial.Colour)]
    public void RecordingTargetDoesNotShowTheGenericSweepMaskSection(HudMaterial material)
    {
        var viewModel = new HudViewModel { CurrentTarget = HudTarget.Recording };

        var sections = viewModel.ActiveTuningSections(AllStatesOn(material));

        Assert.DoesNotContain(HudTuningSection.SweepMask, sections);
        Assert.DoesNotContain(HudTuningSection.HueRotation, sections);
        Assert.Contains(HudTuningSection.Recording, sections);
    }

    // A cone's placement is unreadable in flat grey, so the parts previews
    // open on the colour wheel.
    [Fact]
    public void ThePartsPreviewsOpenOnTheColourMaterial()
    {
        var viewModel = new HudViewModel();

        Assert.Equal(HudMaterial.Colour, viewModel.PreviewMaterial);
    }

    // The per-target map is the contract (a section is shown only where its
    // rows reach the preview), so the literal lists, in panel order, are the
    // expectation. Shipping materials, and the parts' radio on Colour.
    [Theory]
    [InlineData(HudTarget.Charging)]
    [InlineData(HudTarget.Recording,
        HudTuningSection.Geometry, HudTuningSection.GreyMaterial, HudTuningSection.Recording,
        HudTuningSection.Gauge, HudTuningSection.Simulation, HudTuningSection.Orientations)]
    [InlineData(HudTarget.Transcribing,
        HudTuningSection.Geometry, HudTuningSection.GreyMaterial,
        HudTuningSection.SweepMask, HudTuningSection.SweepRotation, HudTuningSection.Transcribing,
        HudTuningSection.DigitReveal, HudTuningSection.Simulation, HudTuningSection.Orientations)]
    [InlineData(HudTarget.Rewriting,
        HudTuningSection.Geometry, HudTuningSection.ColourMaterial, HudTuningSection.HueRotation,
        HudTuningSection.SweepMask, HudTuningSection.SweepRotation, HudTuningSection.Rewriting,
        HudTuningSection.DigitReveal, HudTuningSection.Simulation, HudTuningSection.Orientations)]
    [InlineData(HudTarget.Cone,
        HudTuningSection.Geometry, HudTuningSection.ColourMaterial, HudTuningSection.HueRotation,
        HudTuningSection.Orientations)]
    [InlineData(HudTarget.SweepMask,
        HudTuningSection.Geometry, HudTuningSection.SweepMask, HudTuningSection.SweepRotation,
        HudTuningSection.Orientations)]
    [InlineData(HudTarget.MaskedCone,
        HudTuningSection.Geometry, HudTuningSection.ColourMaterial, HudTuningSection.HueRotation,
        HudTuningSection.SweepMask, HudTuningSection.SweepRotation, HudTuningSection.Orientations)]
    [InlineData(HudTarget.Gauge,
        HudTuningSection.Gauge, HudTuningSection.Recording, HudTuningSection.GreyMaterial,
        HudTuningSection.Simulation)]
    [InlineData(HudTarget.DigitReveal,
        HudTuningSection.DigitReveal, HudTuningSection.ColourMaterial, HudTuningSection.SweepMask)]
    public void EachTargetShowsOnlyTheSectionsItsPreviewConsumes(
        HudTarget target, params HudTuningSection[] expected)
    {
        var viewModel = new HudViewModel { CurrentTarget = target };

        Assert.Equal(expected, viewModel.ActiveTuningSections(Shipping));
    }

    // Whichever material is in play, its own section shows and the other's
    // does not; a hue rotation shows only on the Colour material, and only on
    // the targets whose cone spins.
    [Theory]
    [InlineData(HudTarget.Recording, false)]
    [InlineData(HudTarget.Transcribing, true)]
    [InlineData(HudTarget.Rewriting, true)]
    [InlineData(HudTarget.Cone, true)]
    [InlineData(HudTarget.MaskedCone, true)]
    [InlineData(HudTarget.Gauge, false)]
    [InlineData(HudTarget.DigitReveal, false)]
    public void TheMaterialSectionFollowsTheMaterialInPlay(HudTarget target, bool coneSpins)
    {
        foreach (var material in new[] { HudMaterial.Grey, HudMaterial.Colour })
        {
            var viewModel = new HudViewModel { CurrentTarget = target, PreviewMaterial = material };
            bool colour = material == HudMaterial.Colour;

            var sections = viewModel.ActiveTuningSections(AllStatesOn(material));

            Assert.Equal(colour, sections.Contains(HudTuningSection.ColourMaterial));
            Assert.Equal(!colour, sections.Contains(HudTuningSection.GreyMaterial));
            Assert.Equal(colour && coneSpins, sections.Contains(HudTuningSection.HueRotation));
        }
    }

    // Only the named state carries `material`; the two other states and the
    // parts' radio carry the other one, so any wrong source shows.
    private static ConicArcStrokeConfig OnlyStateOn(ProcessingVariant state, HudMaterial material)
    {
        var other = Other(material);
        return AllStatesOn(other) with
        {
            RecordingMaterial    = state == ProcessingVariant.Recording    ? material : other,
            TranscribingMaterial = state == ProcessingVariant.Transcribing ? material : other,
            RewritingMaterial    = state == ProcessingVariant.Rewriting    ? material : other,
        };
    }

    private static HudMaterial Other(HudMaterial material)
        => material == HudMaterial.Colour ? HudMaterial.Grey : HudMaterial.Colour;

    // A state target reads its own state's material, never another state's
    // nor the parts' radio. The gauge opens during Recording, so it reads
    // Recording's.
    [Theory]
    [InlineData(HudTarget.Recording, ProcessingVariant.Recording)]
    [InlineData(HudTarget.Transcribing, ProcessingVariant.Transcribing)]
    [InlineData(HudTarget.Rewriting, ProcessingVariant.Rewriting)]
    [InlineData(HudTarget.Gauge, ProcessingVariant.Recording)]
    public void AStateTargetShowsItsOwnStatesMaterial(HudTarget target, ProcessingVariant state)
    {
        foreach (var material in new[] { HudMaterial.Grey, HudMaterial.Colour })
        {
            var viewModel = new HudViewModel { CurrentTarget = target, PreviewMaterial = Other(material) };

            Assert.Equal(material, viewModel.MaterialInPlay(OnlyStateOn(state, material)));
        }
    }

    // A part reads the radio, never a state's material.
    [Theory]
    [InlineData(HudTarget.Cone)]
    [InlineData(HudTarget.SweepMask)]
    [InlineData(HudTarget.MaskedCone)]
    [InlineData(HudTarget.DigitReveal)]
    public void APartShowsTheRadiosMaterial(HudTarget target)
    {
        foreach (var material in new[] { HudMaterial.Grey, HudMaterial.Colour })
        {
            var viewModel = new HudViewModel { CurrentTarget = target, PreviewMaterial = material };

            Assert.Equal(material, viewModel.MaterialInPlay(AllStatesOn(Other(material))));
        }
    }
}
