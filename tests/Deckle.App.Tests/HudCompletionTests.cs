using Deckle.Transcription;
using Xunit;

namespace Deckle.App.Tests;

// What the HUD says when a take is over follows what the engine delivered.
[Trait("Category", "regression")]
public sealed class HudCompletionTests
{
    [Fact]
    public void A_take_copied_to_the_clipboard_says_copied()
        => Assert.Equal(HudCompletionMessage.Copied, HudCompletion.For(TranscriptionOutcome.ClipboardOnly));

    [Fact]
    public void A_take_saved_to_a_file_says_file_saved()
        => Assert.Equal(HudCompletionMessage.FileSaved, HudCompletion.For(TranscriptionOutcome.SavedToFile));

    [Fact]
    public void A_take_that_delivered_nothing_clears_the_hud()
        => Assert.Equal(HudCompletionMessage.Hidden, HudCompletion.For(TranscriptionOutcome.None));
}
