using Deckle.Input.PrecisionScroll;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "unit")]
public sealed class PrecisionScrollTuningTests
{
    [Fact]
    public void NormalizeKeepsEveryValueInsideItsPublishedRange()
    {
        var tuning = new PrecisionScrollTuning
        {
            DistancePerDetentMm = double.PositiveInfinity,
            InitialStepDurationMs = double.NegativeInfinity,
            QuietPeriodScale = 0,
        }.Normalize();

        Assert.Equal(
            PrecisionScrollTuning.DistancePerDetentMaximum,
            tuning.DistancePerDetentMm);
        Assert.Equal(
            PrecisionScrollTuning.InitialStepDurationMinimum,
            tuning.InitialStepDurationMs);
        Assert.Equal(
            PrecisionScrollTuning.QuietPeriodScaleMinimum,
            tuning.QuietPeriodScale);
    }

    [Fact]
    public void DefaultsNormalizeWithoutChangingCalibration()
    {
        var defaults = new PrecisionScrollTuning();

        Assert.Equal(defaults, defaults.Normalize());
    }
}
