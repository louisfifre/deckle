using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class ParagraphDraftTests
{
    [Fact]
    public void CloseReturnsTheObservedParagraphAfterBackspace()
    {
        var draft = new ParagraphDraft();
        draft.Append("un paragraphe faut");
        draft.Backspace();
        draft.Backspace();
        draft.Backspace();
        draft.Backspace();
        draft.Append("propre");

        Assert.True(draft.TryClose(out string paragraph));
        Assert.Equal("un paragraphe propre", paragraph);
    }

    [Fact]
    public void OpaqueCaretMoveWithholdsTheCurrentParagraph()
    {
        var draft = new ParagraphDraft();
        draft.Append("texte partiel");

        draft.Invalidate();

        Assert.False(draft.TryClose(out _));
    }

    [Fact]
    public void AppliedCorrectionUpdatesTheObservedOccurrence()
    {
        var draft = new ParagraphDraft();
        draft.Append("on vérifie et ca continue");

        draft.ApplyCorrection("ca", "ça");

        Assert.True(draft.TryClose(out string paragraph));
        Assert.Equal("on vérifie et ça continue", paragraph);
    }

    [Fact]
    public void AmbiguousAppliedCorrectionInvalidatesInsteadOfPickingAnOccurrence()
    {
        var draft = new ParagraphDraft();
        draft.Append("ca marche et ca continue");

        draft.ApplyCorrection("ca", "ça");

        Assert.False(draft.TryClose(out _));
    }

    [Fact]
    public void UnknownAppliedCorrectionInvalidatesInsteadOfGuessing()
    {
        var draft = new ParagraphDraft();
        draft.Append("texte observé");

        draft.ApplyCorrection("absent", "présent");

        Assert.False(draft.TryClose(out _));
    }
}
