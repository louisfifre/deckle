using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class SentenceBoundaryTests
{
    [Fact]
    public void TextEndingOnATerminatorHasNoCarry()
    {
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail("Un. Deux !");

        Assert.Equal("Un. Deux !", complete);
        Assert.Equal("", carry);
    }

    [Fact]
    public void TailAfterTheLastTerminatorIsCarried()
    {
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail("Un. Deux. et puis");

        Assert.Equal("Un. Deux.", complete);
        Assert.Equal("et puis", carry);
    }

    [Theory]
    [InlineData("Il a dit « oui. » et puis", "Il a dit « oui. »", "et puis")]
    [InlineData("Il a dit « oui. » et puis", "Il a dit « oui. »", "et puis")]
    [InlineData("He said \"yes.\" and then", "He said \"yes.\"", "and then")]
    [InlineData("Voilà (enfin !) donc", "Voilà (enfin !)", "donc")]
    public void TerminatorFollowedByAClosingMarkEndsAfterTheMark(
        string text, string expectedComplete, string expectedCarry)
    {
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail(text);

        Assert.Equal(expectedComplete, complete);
        Assert.Equal(expectedCarry, carry);
    }

    [Fact]
    public void TextWithoutTerminatorIsCompleteAsIs()
    {
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail("pas de ponctuation du tout");

        Assert.Equal("pas de ponctuation du tout", complete);
        Assert.Equal("", carry);
    }

    [Fact]
    public void EllipsisEndsASentence()
    {
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail("Je ne sais pas… peut-être demain");

        Assert.Equal("Je ne sais pas…", complete);
        Assert.Equal("peut-être demain", carry);
    }

    [Fact]
    public void DecimalPointIsNotASentenceBoundary()
    {
        var (complete, carry) = SentenceBoundary.SplitUnfinishedTail("Il pèse 3.5 kilos et");

        Assert.Equal("Il pèse 3.5 kilos et", complete);
        Assert.Equal("", carry);
    }
}
