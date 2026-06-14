using System.IO;
using Deckle.Input.Autocorrect;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The form→frequency store: parsing rules (comments, malformed lines), the
// additive merge of duplicate forms, and the lowercase/NFC normalization the
// gate's lookups depend on.
[Trait("Category", "unit")]
public class FrequencyLexiconTests
{
    private static FrequencyLexicon Load(string tsv) =>
        FrequencyLexicon.LoadTsv(new StringReader(tsv));

    [Fact]
    public void ParsesFormAndFrequency()
    {
        var lex = Load("français\t400\nécole\t200\n");

        Assert.Equal(2, lex.Count);
        Assert.True(lex.Contains("français"));
        Assert.Equal(400.0, lex.FrequencyOf("français"));
        Assert.Equal(200.0, lex.FrequencyOf("école"));
    }

    [Fact]
    public void AbsentFormHasZeroFrequency()
    {
        var lex = Load("école\t200\n");

        Assert.False(lex.Contains("inconnu"));
        Assert.Equal(0.0, lex.FrequencyOf("inconnu"));
    }

    [Fact]
    public void SkipsCommentsAndBlankLines()
    {
        var lex = Load("# header\nécole\t200\n\n# trailing\n");

        Assert.Equal(1, lex.Count);
        Assert.Equal(0, lex.SkippedLines); // comments/blanks are not "skipped" malformed lines.
    }

    [Fact]
    public void CountsMalformedLines()
    {
        // No tab, and a non-numeric frequency — both malformed, both counted.
        var lex = Load("école\t200\nnotabhere\nbad\tNaNN\n");

        Assert.Equal(1, lex.Count);
        Assert.Equal(2, lex.SkippedLines);
    }

    [Fact]
    public void DuplicateFormsSumFrequencies()
    {
        // Lexique splits a surface form across rows (POS variants); merge is additive.
        var lex = Load("marche\t85\nmarche\t40\n");

        Assert.Equal(1, lex.Count);
        Assert.Equal(125.0, lex.FrequencyOf("marche"));
    }

    [Fact]
    public void FormsAreLowercased()
    {
        var lex = Load("École\t200\n");

        Assert.True(lex.Contains("école"));
        Assert.False(lex.Contains("École"));
    }
}
