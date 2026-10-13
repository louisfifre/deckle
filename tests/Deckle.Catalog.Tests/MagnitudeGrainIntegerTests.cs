using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// A magnitude over a count (a beam size, a number of cells) declares Integer, and
// its grain never drops below one: a plain range that short would grain at a
// fraction and show decimals the model cannot hold.
[Trait("Category", "unit")]
public class MagnitudeGrainIntegerTests
{
    [Theory]
    [InlineData(1, 10)]   // beam size
    [InlineData(4, 24)]   // border cells
    public void AShortIntegerRangeGrainsAtOneWhereAPlainRangeWouldGrainAtAFraction(double minimum, double maximum)
    {
        Assert.True(MagnitudeGrain.StepFor(minimum, maximum) < 1);
        Assert.Equal(1, MagnitudeGrain.StepFor(minimum, maximum, integer: true));
    }

    [Fact]
    public void AWideIntegerRangeKeepsItsDerivedGrain()
    {
        // 0..254 brightness: the plain rule already gives a whole step.
        Assert.Equal(10, MagnitudeGrain.StepFor(0, 254, integer: true));
    }

    [Fact]
    public void AnIntegerGrainShowsNoDecimals()
    {
        Assert.Equal(0, MagnitudeGrain.DecimalsFor(MagnitudeGrain.StepFor(1, 10, integer: true)));
    }
}
