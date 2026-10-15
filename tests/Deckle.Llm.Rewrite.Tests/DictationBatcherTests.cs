using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Threshold 20 throughout unless stated, so the hard cap is 40.
[Trait("Category", "unit")]
public sealed class DictationBatcherTests
{
    [Fact]
    public void ShortUtterancesTravelTogetherAndCloseAtTheFirstPausePastTheThreshold()
    {
        var batcher = new DictationBatcher(20);

        Assert.Null(batcher.Push("Un.", endedOnSilence: true));
        Assert.Null(batcher.Push("Deux trois.", endedOnSilence: false));
        Assert.Null(batcher.Push("Quatre cinq.", endedOnSilence: false));
        DictationBatch? batch = batcher.Push("Six.", endedOnSilence: true);

        Assert.NotNull(batch);
        Assert.Equal(0, batch.Index);
        Assert.Equal("Un.\n\nDeux trois. Quatre cinq. Six.", batch.Text);
        Assert.True(batch.EndsOnPause);
    }

    [Fact]
    public void LongUtterancePastTheHardCapClosesAloneWithoutAPause()
    {
        var batcher = new DictationBatcher(20);
        const string longSentence = "Une phrase longue qui dépasse largement le plafond.";

        DictationBatch? batch = batcher.Push(longSentence, endedOnSilence: false);

        Assert.NotNull(batch);
        Assert.Equal(longSentence, batch.Text);
        Assert.False(batch.EndsOnPause);
    }

    [Fact]
    public void BatchNeverClosesUnderTheThreshold()
    {
        var batcher = new DictationBatcher(100);

        Assert.Null(batcher.Push("Court.", endedOnSilence: true));
        Assert.Null(batcher.Push("Encore court.", endedOnSilence: true));
        Assert.Null(batcher.Push("Toujours court.", endedOnSilence: true));
    }

    [Fact]
    public void UnfinishedTailIsCarriedIntoTheNextBatch()
    {
        var batcher = new DictationBatcher(20);

        DictationBatch? first = batcher.Push("Première phrase bien finie ici. Et puis ensuite", endedOnSilence: false);
        DictationBatch? second = batcher.Push("on continue.", endedOnSilence: true);

        Assert.NotNull(first);
        Assert.Equal("Première phrase bien finie ici.", first.Text);
        Assert.NotNull(second);
        Assert.Equal(1, second.Index);
        Assert.Equal("Et puis ensuite on continue.", second.Text);
    }

    [Fact]
    public void PauseAfterACarriedFragmentBecomesAParagraphBreakInsideTheNextBatch()
    {
        var batcher = new DictationBatcher(20);

        batcher.Push("Phrase finie. Et puis", endedOnSilence: true);
        DictationBatch? next = batcher.Push("Nouvelle idée ici.", endedOnSilence: true);

        Assert.NotNull(next);
        Assert.Equal("Et puis\n\nNouvelle idée ici.", next.Text);
    }

    [Fact]
    public void EndsOnPauseIsFalseWhenACarryHappened()
    {
        var batcher = new DictationBatcher(20);

        DictationBatch? batch = batcher.Push("Phrase finie. Et puis", endedOnSilence: true);

        Assert.NotNull(batch);
        Assert.Equal("Phrase finie.", batch.Text);
        Assert.False(batch.EndsOnPause);
    }

    [Fact]
    public void ParagraphBreakBeforeACarriedUtteranceStaysAtTheSeam()
    {
        var batcher = new DictationBatcher(30);

        batcher.Push("Une première phrase.", endedOnSilence: true);
        DictationBatch? batch = batcher.Push("Et puis ensuite", endedOnSilence: true);

        Assert.NotNull(batch);
        Assert.Equal("Une première phrase.", batch.Text);
        Assert.True(batch.EndsOnPause);
    }

    [Fact]
    public void TextWithNoTerminatorIsSentWhole()
    {
        var batcher = new DictationBatcher(20);
        const string unpunctuated = "une dictée sans aucune ponctuation du tout";

        DictationBatch? batch = batcher.Push(unpunctuated, endedOnSilence: false);

        Assert.NotNull(batch);
        Assert.Equal(unpunctuated, batch.Text);
    }

    [Fact]
    public void FlushReturnsTheRemainderWithoutCarry()
    {
        var batcher = new DictationBatcher(20);
        batcher.Push("Phrase finie. Et puis", endedOnSilence: false);

        DictationBatch? rest = batcher.Flush();

        Assert.NotNull(rest);
        Assert.Equal("Phrase finie. Et puis", rest.Text);
    }

    [Fact]
    public void FlushReturnsNullWhenNothingRemains()
    {
        var batcher = new DictationBatcher(20);
        batcher.Push("Reste", endedOnSilence: false);
        batcher.Flush();

        Assert.Null(batcher.Flush());
    }

    [Fact]
    public void BlankUtteranceIsIgnored()
    {
        var batcher = new DictationBatcher(20);

        Assert.Null(batcher.Push("   ", endedOnSilence: true));
        Assert.Null(batcher.Flush());
    }
}
