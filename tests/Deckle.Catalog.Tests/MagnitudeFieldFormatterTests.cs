using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The number field of a magnitude writes a value as a person does: "0.1", not
// "0.10", while still capping the readout at the grain. Bug: FractionDigits was
// set to the grain's decimals, and it is a minimum, so a 0.05 grain padded 0.1
// into "0.10", 0.5 into "0.50" and 0.6 into "0.60".
[Trait("Category", "regression")]
public class MagnitudeFieldFormatterTests
{
    // The formatter follows the machine's decimal separator; compare on '.'.
    private static string Read(double value, int decimals) =>
        MagnitudeGrain.FieldFormatter(decimals).FormatDouble(value).Replace(',', '.');

    [Theory]
    [InlineData(0.1, 2, "0.1")]
    [InlineData(0.5, 2, "0.5")]
    [InlineData(0.6, 2, "0.6")]
    [InlineData(0.15, 2, "0.15")]
    [InlineData(0.15000000000000002, 2, "0.15")]
    [InlineData(0.1534, 2, "0.15")]
    [InlineData(1, 1, "1")]
    [InlineData(1200, 0, "1200")]
    [InlineData(-45, 0, "-45")]
    public void AValueIsWrittenWithOnlyTheDecimalsItNeedsAndNeverBeyondTheGrain(double value, int decimals, string expected)
    {
        Assert.Equal(expected, Read(value, decimals));
    }

    [Fact]
    public void TheFieldAndTheResetTooltipWriteTheSameText()
    {
        foreach (double value in new[] { 0.1, 0.5, 0.25, 0.03, 2 })
            Assert.Equal(
                Read(value, 2),
                DefaultValueText.Magnitude(value, 2, null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ATypedFigureParsesBackToTheSameValue()
    {
        var formatter = MagnitudeGrain.FieldFormatter(2);
        Assert.Equal(0.1, formatter.ParseDouble(formatter.FormatDouble(0.1)));
    }
}
