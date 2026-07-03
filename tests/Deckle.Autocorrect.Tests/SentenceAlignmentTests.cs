using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Inverts SentenceCorpus.Flush: the typed side re-tokenizes with the canonical
// tokenizer, the final side overlays the History transitions by slot index. These
// pin the alignment against the very records SentenceCorpusTests shows the
// accumulator emitting.
[Trait("Category", "unit")]
public sealed class SentenceAlignmentTests
{
    private static (string[] Typed, string[] Final) Align(string typed, string final, string history)
    {
        var (t, f) = SentenceAlignment.Align(new SentenceCorpus.SentenceRecord(typed, final, history));
        return (t.ToArray(), f.ToArray());
    }

    [Fact]
    public void UnchangedSentenceAlignsTypedToItself()
    {
        var (typed, final) = Align("bonjour monde.", "bonjour monde.", "");

        Assert.Equal(new[] { "bonjour", "monde" }, typed);
        Assert.Equal(new[] { "bonjour", "monde" }, final);
    }

    [Fact]
    public void OverlaysCommitStageFinalFormsBySlot()
    {
        // The record SentenceCorpusTests.RecordsTheCommitStageTransition emits.
        var (typed, final) = Align(
            "marche ecole.", "marché école.", "#0=marche»commit:marché|#1=ecole»commit:école");

        Assert.Equal(new[] { "marche", "ecole" }, typed);
        Assert.Equal(new[] { "marché", "école" }, final);
    }

    [Fact]
    public void TakesTheLastTransitionAsTheFinalForm()
    {
        // « etant » retyped by hand then commit-repaired: user»commit, last wins.
        var (typed, final) = Align(
            "etant la.", "étant là.", "#0=etant»user:etant»commit:étant|#1=la»sentence:là");

        Assert.Equal(new[] { "etant", "la" }, typed);
        Assert.Equal(new[] { "étant", "là" }, final);
    }

    [Fact]
    public void TokenizesTheElisionApostropheAsTwoSlots()
    {
        var (typed, final) = Align("j'ai faim.", "j'ai faim.", "");

        Assert.Equal(new[] { "j'", "ai", "faim" }, typed);
        Assert.Equal(new[] { "j'", "ai", "faim" }, final);
    }

    [Fact]
    public void IgnoresAnOutOfRangeSlotReference()
    {
        // A history pointing past the tokenized slots must not throw.
        var (_, final) = Align("un deux.", "un deux.", "#9=x»commit:y");

        Assert.Equal(new[] { "un", "deux" }, final);
    }

    [Fact]
    public void ParsesEachChangedSlotOnce()
    {
        var parsed = SentenceAlignment.ParseHistory(
            "#0=marche»commit:marché|#1=ecole»commit:école").ToArray();

        Assert.Equal(new[] { (0, "marché"), (1, "école") }, parsed);
    }
}
