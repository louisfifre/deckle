using System.Collections.Generic;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The text-corpus accumulator — the substrate of the « what I typed » dataset.
// These pin the two things the corpus must get right: faithful reconstruction of
// both sides (so a keyboard substitution like ';' for an apostrophe survives to
// be mined) and the purity rule (a sentence interrupted before its end is dropped,
// never half-recorded).
[Trait("Category", "unit")]
public class SentenceCorpusTests
{
    private static (SentenceCorpus corpus, List<(string Typed, string Final)> done) New()
    {
        var done = new List<(string, string)>();
        var corpus = new SentenceCorpus { Completed = (t, f) => done.Add((t, f)) };
        return (corpus, done);
    }

    [Fact]
    public void EmitsTypedAndFinalOnSentenceEnd()
    {
        var (c, done) = New();
        c.Word("marche", "marché", ' ');
        c.Word("ecole", "école", '.');

        Assert.Single(done);
        Assert.Equal("marche ecole.", done[0].Typed);
        Assert.Equal("marché école.", done[0].Final);
    }

    [Fact]
    public void PreservesTheSemicolonForApostropheSubstitution()
    {
        // « l;ecole » — the ';' is a boundary the corrector never repairs (it
        // splits the token), so it must survive on BOTH sides to be mined.
        var (c, done) = New();
        c.Word("l", "l", ';');
        c.Word("ecole", "école", '.');

        Assert.Equal("l;ecole.", done[0].Typed);
        Assert.Equal("l;école.", done[0].Final);
    }

    [Fact]
    public void CollapsesTheElisionApostropheSeparator()
    {
        // The tracker attaches the elision apostrophe to the word (« j' »); the
        // rejoin must not double it into « j'' ».
        var (c, done) = New();
        c.Word("j'", "j'", '\'');
        c.Word("ai", "ai", '.');

        Assert.Equal("j'ai.", done[0].Typed);
    }

    [Fact]
    public void DropsAPartialSentenceOnAContaminatingReset()
    {
        // A Ctrl-chord (possible paste) before any sentence end: the run is
        // untrustworthy and must never be emitted.
        var (c, done) = New();
        c.Word("bonjour", "bonjour", ' ');
        c.Reset(ResetReason.Shortcut);

        Assert.Empty(done);
    }

    [Fact]
    public void EnterEndsASentence()
    {
        var (c, done) = New();
        c.Word("bonjour", "bonjour", ' ');
        c.Word("monde", "monde", ' ');
        c.Reset(ResetReason.Enter);

        Assert.Single(done);
        Assert.Equal("bonjour monde ", done[0].Typed);
    }

    [Fact]
    public void MergesAManualReEditKeepingTheTypedError()
    {
        // « etant » left literal, then backspaced and retyped « étant »: the
        // TYPED side keeps the error, the FINAL side takes the fix — one labelled
        // pair, not a duplicated token.
        var (c, done) = New();
        c.Word("etant", "etant", ' ');   // first commit, left alone
        c.Word("étant", "étant", ' ');   // the re-commit appends a slot…
        c.Edit("etant", "étant");        // …folded back into the slot it edited
        c.Word("la", "là", '.');

        Assert.Equal("etant la.", done[0].Typed);
        Assert.Equal("étant là.", done[0].Final);
    }

    [Fact]
    public void EmitsEachSentenceSeparately()
    {
        var (c, done) = New();
        c.Word("a", "a", '.');
        c.Word("b", "b", '!');

        Assert.Equal(2, done.Count);
        Assert.Equal("a.", done[0].Typed);
        Assert.Equal("b!", done[1].Typed);
    }
}
