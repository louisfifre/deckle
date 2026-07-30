using Deckle.Autocorrect;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class VerifiedCaretSentenceTests
{
    [Fact]
    public void SameTargetAndExactSentenceRemainValid()
    {
        FocusedCaretText snapshot = Snapshot("Avant. Phrase fautive.");
        var verified = new VerifiedCaretSentence(snapshot, "Phrase fautive.");

        Assert.True(verified.Matches(Snapshot("Bruit différent. Phrase fautive.")));
    }

    [Fact]
    public void ChangedSentenceExpiresTheEvidence()
    {
        FocusedCaretText snapshot = Snapshot("Avant. Phrase fautive.");
        var verified = new VerifiedCaretSentence(snapshot, "Phrase fautive.");

        Assert.False(verified.Matches(Snapshot("Avant. Phrase modifiée.")));
    }

    [Fact]
    public void ChangedTargetExpiresTheEvidence()
    {
        FocusedCaretText snapshot = Snapshot("Avant. Phrase fautive.");
        var verified = new VerifiedCaretSentence(snapshot, "Phrase fautive.");

        Assert.False(verified.Matches(Snapshot("Avant. Phrase fautive.", processId: 99)));
    }

    private static FocusedCaretText Snapshot(string text, int processId = 42) => new(
        text,
        ReachedDocumentStart: false,
        MovedCharacters: text.Length,
        ProcessId: processId,
        ControlType: 50004,
        NativeWindowHandle: 0,
        ForegroundWindow: 1234,
        RuntimeId: "42.1.2",
        Pattern: "text_selection");
}
