using Xunit;

namespace Deckle.Home.Tests;

// A type's name is the singular the app shows for one object; the plural is
// its own entry. The names must not drift back to plural forms.
[Trait("Category", "unit")]
public sealed class HomeTermsTests
{
    [Theory]
    [InlineData("circuit", "Circuit", "Circuits")]
    [InlineData("panel", "Tableau", "Tableaux")]
    [InlineData("system", "Système", "Systèmes")]
    [InlineData("device", "Appareil", "Appareils")]
    [InlineData("component", "Composant", "Composants")]
    [InlineData("plant", "Plante", "Plantes")]
    public void ATypeNameIsSingularAndItsPluralStaysPlural(string type, string name, string plural)
    {
        Assert.Equal(name, HomeTerms.Current.TypeName(type));
        Assert.Equal(plural, HomeTerms.Current.TypePluralName(type));
    }
}