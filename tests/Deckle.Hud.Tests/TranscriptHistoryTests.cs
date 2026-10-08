using Xunit;

namespace Deckle.Hud.Tests;

[Trait("Category", "unit")]
public sealed class TranscriptHistoryTests
{
    [Fact]
    public void Text_JoinsWithASpace_WhenThePreviousUtteranceDidNotEndOnSilence()
    {
        var history = new TranscriptHistory(maxCharacters: 1000);

        history.Append("bonjour", endedOnSilence: false);
        history.Append("Deckle", endedOnSilence: true);

        Assert.Equal("bonjour Deckle", history.Text);
    }

    [Fact]
    public void Text_JoinsWithABlankLine_AfterASilenceCut()
    {
        var history = new TranscriptHistory(maxCharacters: 1000);

        history.Append("bonjour", endedOnSilence: true);
        history.Append("Deckle", endedOnSilence: false);
        history.Append("encore", endedOnSilence: false);

        Assert.Equal("bonjour\n\nDeckle encore", history.Text);
        Assert.Equal(3, history.UtteranceCount);
    }

    [Fact]
    public void Append_OverBudget_DropsTheOldestUtterancesFirst()
    {
        var history = new TranscriptHistory(maxCharacters: 12);

        history.Append("aaaa", endedOnSilence: false);
        history.Append("bbbb", endedOnSilence: true);
        history.Append("cccc", endedOnSilence: false);

        Assert.Equal("bbbb\n\ncccc", history.Text);
        Assert.Equal(2, history.UtteranceCount);
    }

    [Fact]
    public void Append_KeepsTheNewestUtterance_EvenWhenItAloneExceedsTheBudget()
    {
        var history = new TranscriptHistory(maxCharacters: 5);

        history.Append("aa", endedOnSilence: false);
        history.Append("a long utterance", endedOnSilence: false);

        Assert.Equal("a long utterance", history.Text);
        Assert.Equal(1, history.UtteranceCount);
    }

    [Fact]
    public void Clear_StartsAFreshTake()
    {
        var history = new TranscriptHistory(maxCharacters: 1000);
        history.Append("bonjour", endedOnSilence: true);

        history.Clear();
        history.Append("Deckle", endedOnSilence: false);

        Assert.Equal("Deckle", history.Text);
        Assert.Equal(1, history.UtteranceCount);
    }
}
