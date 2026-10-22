using Xunit;

namespace Deckle.Core.Tests;

// A DIP is one pixel at 96 DPI and grows with the monitor's scale; the
// pixel figure is whole, halves rounding up.
[Trait("Category", "unit")]
public sealed class DpiTests
{
    [Theory]
    [InlineData(96u,  1.0)]
    [InlineData(120u, 1.25)]
    [InlineData(144u, 1.5)]
    [InlineData(240u, 2.5)]
    public void ScaleIsTheDpiOverTheReference(uint dpi, double scale)
    {
        Assert.Equal(scale, Dpi.ScaleOf(dpi));
    }

    // A literal DIP figure on purpose: re-tuning a Spacing step must not break
    // a test about the conversion.
    [Theory]
    [InlineData(1.0,  64)]
    [InlineData(1.25, 80)]
    [InlineData(1.5,  96)]
    [InlineData(2.5,  160)]
    public void AStepOfTheScaleLandsOnWholePixels(double dpiScale, int pixels)
    {
        const int SixtyFourDips = 64;

        Assert.Equal(pixels, Dpi.ToPixels(SixtyFourDips, dpiScale));
    }

    [Fact]
    public void AHalfPixelRoundsUp()
    {
        // 5 DIP at 150% is 7.5 px: 8, not the banker's 8-or-7.
        Assert.Equal(8, Dpi.ToPixels(5, 1.5));
        // 3 DIP at 150% is 4.5 px: 5, where ToEven would give 4.
        Assert.Equal(5, Dpi.ToPixels(3, 1.5));
    }
}
