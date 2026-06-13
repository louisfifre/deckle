using System.IO;
using Deckle.Input.Autocorrect.Lexicon;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The reverse map: folded key → accented variants, frequency-ranked. Only
// diacritic-bearing forms enter; a literal equal to its own fold does not.
[Trait("Category", "unit")]
public class AccentIndexTests
{
    private static AccentIndex Build(string tsv) =>
        AccentIndex.Build(FrequencyLexicon.LoadTsv(new StringReader(tsv)));

    [Fact]
    public void BucketsAccentedFormsUnderTheirFoldedKey()
    {
        var index = Build("école\t200\nélève\t30\nélevé\t25\n");

        var ecole = index.VariantsOf("ecole");
        Assert.Single(ecole);
        Assert.Equal("école", ecole[0].Form);
    }

    [Fact]
    public void VariantsAreSortedByFrequencyDescending()
    {
        // Two forms fold to "eleve" — the more frequent must come first.
        var index = Build("élève\t30\nélevé\t25\n");

        var eleve = index.VariantsOf("eleve");
        Assert.Equal(2, eleve.Count);
        Assert.Equal("élève", eleve[0].Form);
        Assert.Equal("élevé", eleve[1].Form);
    }

    [Fact]
    public void PlainFormsWithoutDiacriticsAreExcluded()
    {
        // "marche" folds to itself — the gate owns it, the index must not.
        var index = Build("marche\t85\nmarché\t90\n");

        var marche = index.VariantsOf("marche");
        Assert.Single(marche);
        Assert.Equal("marché", marche[0].Form);
    }

    [Fact]
    public void UnknownKeyReturnsEmpty()
    {
        var index = Build("école\t200\n");

        Assert.Empty(index.VariantsOf("inconnu"));
    }

    [Fact]
    public void CountIsTheNumberOfDistinctKeys()
    {
        var index = Build("école\t200\nélève\t30\nélevé\t25\n");

        // "ecole" and "eleve" — two keys, even though three forms.
        Assert.Equal(2, index.Count);
    }
}
