using System.Text.Json.Nodes;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// Values compare by meaning: an option by the manifest option its name
// stands for, a number as a number, a body with its whitespace set aside.
[Trait("Category", "unit")]
public sealed class HomeRecreationComparerTests
{
    [Fact]
    public void AnOptionNamedInFrenchOrInEnglishIsTheSameOption()
    {
        Assert.Equal("option:in_progress", HomeRecreationComparer.Canonical("state", "En cours"));
        Assert.Equal("option:in_progress", HomeRecreationComparer.Canonical("state", "In progress"));
        Assert.Equal("option:in_progress", HomeRecreationComparer.Canonical("state", "in_progress"));
    }

    [Fact]
    public void AnOpenOptionComparesByItsNameCaseSetAside() =>
        Assert.Equal(
            HomeRecreationComparer.Canonical("manufacturer", "Marque Fictive"),
            HomeRecreationComparer.Canonical("manufacturer", "marque fictive"));

    [Fact]
    public void ANumberIsANumberWhateverItsJsonForm()
    {
        Assert.Equal(
            HomeRecreationComparer.Expected("quantity", "number", JsonValue.Create(16)),
            HomeRecreationComparer.Expected("quantity", "number", JsonNode.Parse("16.0")));
        Assert.Equal(
            HomeRecreationComparer.Expected("quantity", "number", JsonValue.Create(2.5)),
            HomeRecreationComparer.Expected("quantity", "number", JsonValue.Create("2.5")));
    }

    [Fact]
    public void ARelationComparesItsSelectorsAsASet() =>
        Assert.Equal(
            HomeRecreationComparer.Expected("controls", "objects", new JsonArray("ZZ-LC01", "ZZ-LC02")),
            HomeRecreationComparer.Expected("controls", "objects", new JsonArray("zz-lc02", "ZZ-LC01")));

    [Fact]
    public void ABodyIgnoresHowItsWhitespaceWasRewritten() =>
        Assert.Equal(
            HomeRecreationComparer.NormalizeBody("Corps fictif\n\nNote  fictive\n"),
            HomeRecreationComparer.NormalizeBody("Corps fictif\nNote fictive"));
}
