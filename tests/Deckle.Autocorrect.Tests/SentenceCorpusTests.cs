using System.Collections.Generic;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The text-corpus accumulator — the substrate of the « what I typed » dataset.
// These pin the things the corpus must get right: faithful reconstruction of both
// sides (so a keyboard substitution like ';' for an apostrophe survives to be
// mined), the purity rule (a sentence interrupted before its end is dropped, never
// half-recorded), and the ordered per-slot history (each transition tagged by the
// stage that made it — commit / sentence / user).
[Trait("Category", "unit")]
public class SentenceCorpusTests
{
    private static (SentenceCorpus corpus, List<SentenceCorpus.SentenceRecord> done) New()
    {
        var done = new List<SentenceCorpus.SentenceRecord>();
        var corpus = new SentenceCorpus { Completed = done.Add };
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

    // ── Ordered per-slot history ──────────────────────────────────────────────

    [Fact]
    public void NoHistoryWhenNothingChanged()
    {
        var (c, done) = New();
        c.Word("bonjour", "bonjour", ' ');
        c.Word("monde", "monde", '.');

        Assert.Equal("", done[0].History);
    }

    [Fact]
    public void RecordsTheCommitStageTransition()
    {
        // A commit-stage repair is the slot's first transition, tagged commit.
        var (c, done) = New();
        c.Word("marche", "marché", ' ');
        c.Word("ecole", "école", '.');

        Assert.Equal("#0=marche»commit:marché|#1=ecole»commit:école", done[0].History);
    }

    [Fact]
    public void RecordsASentenceStageRewriteFromBehind()
    {
        // « la » committed literal, then the sentence stage rewrites it to « là »
        // while the sentence is still open — a Sentence transition on its slot.
        var (c, done) = New();
        c.Word("la", "la", ' ');
        c.SentenceEdit("la", "là");       // deferred rewrite, sentence still open
        c.Word("bas", "bas", '.');

        Assert.Equal("la bas.", done[0].Typed);
        Assert.Equal("là bas.", done[0].Final);
        Assert.Equal("#0=la»sentence:là", done[0].History);
    }

    [Fact]
    public void DropsASentenceRewriteThatLandsAfterFlush()
    {
        // A verdict for the last word arriving after the sentence flushed finds no
        // open slot — a post-close edit, invisible by design.
        var (c, done) = New();
        c.Word("la", "la", '.');          // flushes immediately
        c.SentenceEdit("la", "là");       // too late — nothing to attach to

        Assert.Single(done);
        Assert.Equal("la.", done[0].Final);
        Assert.Equal("", done[0].History);
    }

    [Fact]
    public void ManualReEditIsTaggedUserWithTheRetypeCommitCarriedOver()
    {
        // « etant » left literal, retyped as « etant » which the gate then fixes to
        // « étant »: the ordered path is user (the retype) then commit (its repair).
        var (c, done) = New();
        c.Word("etant", "etant", ' ');
        c.Word("etant", "étant", ' ');    // re-commit: user retyped, gate repaired
        c.Edit("etant", "étant");
        c.Word("la", "là", '.');

        Assert.Equal("#0=etant»user:etant»commit:étant|#1=la»commit:là", done[0].History);
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
