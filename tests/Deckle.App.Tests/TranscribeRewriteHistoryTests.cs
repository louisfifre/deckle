using Xunit;

namespace Deckle.App.Tests;

// The window's Undo and Redo: each replacement of the whole text is one step,
// and the text before it is the one read at that moment.
[Trait("Category", "unit")]
public sealed class TranscribeRewriteHistoryTests
{
    [Fact]
    public void TwoUndosAfterTwoRewritesBringBackTheTranscript()
    {
        var history = new TranscribeRewriteHistory();
        history.Record("");             // transcription replaces the empty field
        history.Record("transcript");   // rewrite with Mail pro
        history.Record("mail pro");     // rewrite with Formal

        Assert.Equal("mail pro", history.Undo("formal"));
        Assert.Equal("transcript", history.Undo("mail pro"));
    }

    [Fact]
    public void RedoReturnsWhatUndoLeft()
    {
        var history = new TranscribeRewriteHistory();
        history.Record("before");

        Assert.Equal("before", history.Undo("after"));
        Assert.True(history.CanRedo);
        Assert.Equal("after", history.Redo("before"));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void AHandEditBeforeARewriteIsWhatUndoRestores()
    {
        var history = new TranscribeRewriteHistory();
        history.Record("");
        // The person corrected a word, then rewrote: the corrected text is read.
        history.Record("transcript corrected");

        Assert.Equal("transcript corrected", history.Undo("rewritten"));
    }

    [Fact]
    public void ANewStepForgetsRedo()
    {
        var history = new TranscribeRewriteHistory();
        history.Record("a");
        history.Undo("b");

        history.Record("a");

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo("c"));
    }

    [Fact]
    public void AFailedRunLeavesNoStep()
    {
        var history = new TranscribeRewriteHistory();
        history.Record("pasted");

        history.Withdraw();

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void NothingToUndoAnswersNull()
    {
        var history = new TranscribeRewriteHistory();

        Assert.False(history.CanUndo);
        Assert.Null(history.Undo("text"));
    }
}
