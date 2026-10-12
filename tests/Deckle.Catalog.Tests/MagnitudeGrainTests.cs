using System;
using System.Globalization;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// A magnitude control forwards the slider's raw double to the model. Binary
// arithmetic makes 0.05 + 2 * 0.05 equal 0.15000000000000002, so the grain helper
// must put every detent on the exact decimal the field shows.
[Trait("Category", "regression")]
public class MagnitudeGrainTests
{
    // Reaction speed, Exposure, and a whole-number range.
    [Theory]
    [InlineData(0.05, 1.0)]
    [InlineData(-2.0, 2.0)]
    [InlineData(0.0, 30.0)]
    public void SliderPositionOnTheGrainStoresTheExactDecimalValue(double minimum, double maximum)
    {
        double step = MagnitudeGrain.StepFor(minimum, maximum);
        int decimals = MagnitudeGrain.DecimalsFor(step);
        int positions = (int)Math.Round((maximum - minimum) / step);

        for (int k = 0; k <= positions; k++)
        {
            // The slider's own detent arithmetic, in doubles.
            double raw = minimum + k * step;

            // The expected value is built in decimal, where 0.05 + 2 * 0.05 is exact.
            double exact = (double)((decimal)minimum + k * (decimal)step);
            double expected = exact == 0 ? 0 : exact; // decimal keeps a sign on zero

            double stored = MagnitudeGrain.Snap(raw, decimals);

            Assert.Equal(expected, stored);
            Assert.Equal(
                expected.ToString("R", CultureInfo.InvariantCulture),
                stored.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void ValueTypedFinerThanTheGrainIsStoredAtTheGrain()
    {
        Assert.Equal(0.15, MagnitudeGrain.Snap(0.1534, 2));
    }

    [Fact]
    public void NegativeDustAroundZeroIsStoredAsPlainZero()
    {
        double stored = MagnitudeGrain.Snap(-1e-17, 1);

        Assert.Equal(0.0, stored);
        Assert.False(double.IsNegative(stored));
    }
}
