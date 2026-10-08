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
}
