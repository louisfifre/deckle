using System.Globalization;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The reset tooltip is the one place a card states its default, so the default
// must read exactly like the control beside it: a magnitude on its display
// grain with its unit, a choice by its option label, a text as typed. A default
// with no readable form yields null, which the composer turns into the generic
// "Reset to default".
[Trait("Category", "unit")]
public class DefaultValueTextTests
{
    [Theory]
    [InlineData(0.5, 2, null, "0.50")]
    [InlineData(250, 0, "ms", "250 ms")]
    [InlineData(-45, 0, "dBFS", "-45 dBFS")]
    [InlineData(0.15000000000000002, 2, null, "0.15")]
    [InlineData(5, 0, null, "5")]
    public void AMagnitudeReadsOnItsGrainWithItsUnit(double value, int decimals, string? unit, string expected)
    {
        Assert.Equal(expected, DefaultValueText.Magnitude(value, decimals, unit, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AChoiceReadsAsTheLabelOfTheOptionThatCarriesTheDefault()
    {
        var args = new ChoiceArgs([new ChoiceOption(0, "LimitNone"), new ChoiceOption(5, "LimitFive")]);

        Assert.Equal("No limit", DefaultValueText.Choice(args, 0, key => key == "LimitNone" ? "No limit" : "5 min"));
    }

    [Fact]
    public void AChoiceWhoseDefaultNoOptionCarriesHasNoReadableForm()
    {
        var args = new ChoiceArgs([new ChoiceOption("Dark", "ThemeDark")]);

        Assert.Null(DefaultValueText.Choice(args, "Light", key => key));
    }

    [Fact]
    public void ATextReadsAsItselfAndABlankOneHasNoReadableForm()
    {
        Assert.Equal("http://localhost:11434", DefaultValueText.Text("http://localhost:11434"));
        Assert.Null(DefaultValueText.Text(""));
        Assert.Null(DefaultValueText.Text("   "));
    }
}
