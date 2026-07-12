using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The text-corpus accumulator — the substrate of the « what I typed » dataset.
// These pin the things the corpus must get right: faithful reconstruction of both
// sides (so a keyboard substitution like ';' for an apostrophe survives to be
// mined), the closure tagging (a run ends "sentence", "enter" or "interrupted", and
// an interrupted run is now EMITTED — still verbatim keyboard input — not dropped),
// the typed/final separator split that keeps both sides tokenizing to the same slot
// count, the typing-rhythm timing string, and the ordered per-slot history (each
// transition tagged by the stage that made it — commit / sentence / user).
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
    public void EmitsAnInterruptedRunTaggedInterrupted()
    {
        // A Ctrl-chord (possible paste) before any sentence end: the run is still
        // verbatim keyboard input (paste never reaches the word stream), so it is
        // emitted with its slots and tagged "interrupted" — no longer dropped.
        var (c, done) = New();
        c.Word("bonjour", "bonjour", ' ');
        c.Reset(ResetReason.Shortcut);

        var rec = Assert.Single(done);
        Assert.Equal("bonjour ", rec.Typed);
        Assert.Equal("interrupted", rec.Closure);
    }

    [Fact]
    public void AnInterruptedEmptyRunEmitsNothing()
    {
        // No word accumulated before the reset — nothing to emit.
        var (c, done) = New();
        c.Reset(ResetReason.Navigation);

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

    // ── Closure tagging ───────────────────────────────────────────────────────

    [Theory]
    [InlineData('.')]
    [InlineData('!')]
    [InlineData('?')]
    public void TagsASentenceBoundaryAsClosureSentence(char ender)
    {
        var (c, done) = New();
        c.Word("a", "a", ender);

        Assert.Equal("sentence", done[0].Closure);
    }

    [Fact]
    public void TagsAnEnterResetAsClosureEnter()
    {
        var (c, done) = New();
        c.Word("a", "a", ' ');
        c.Reset(ResetReason.Enter);

        Assert.Equal("enter", done[0].Closure);
    }

    [Theory]
    [InlineData(ResetReason.FocusChanged)]
    [InlineData(ResetReason.Navigation)]
    [InlineData(ResetReason.DeadKey)]
    public void TagsEveryOtherResetAsClosureInterrupted(ResetReason reason)
    {
        var (c, done) = New();
        c.Word("a", "a", ' ');
        c.Reset(reason);

        Assert.Equal("interrupted", done[0].Closure);
    }

    // ── Timing ────────────────────────────────────────────────────────────────

    [Fact]
    public void TimingIsPerSlotInterCommitGapsWithTheFirstSlotZero()
    {
        // Gaps are the ms elapsed since the previous slot's commit; the first is "0".
        var (c, done) = New();
        c.Word("a", "a", ' ', 1000);
        c.Word("b", "b", ' ', 1340);
        c.Word("c", "c", '.', 2560);

        Assert.Equal("0,340,1220", done[0].Timing);
    }

    [Fact]
    public void TimingIsEmptyWhenNoTimestampsAreAvailable()
    {
        // A caller without a clock passes 0 throughout — the whole string is
        // unavailable rather than a run of zeros.
        var (c, done) = New();
        c.Word("a", "a", ' ');
        c.Word("b", "b", '.');

        Assert.Equal("", done[0].Timing);
    }

    // ── Typed/final separator integrity ───────────────────────────────────────

    [Fact]
    public void ReEditOnAnElisionApostropheDoesNotFuseTheTypedSide()
    {
        // « de » committed, then backspaced and retyped as the elision « d' », whose
        // display separator is empty. The FINAL side rejoins « d'avoir » on the
        // attached apostrophe; the TYPED side must keep « de »'s own boundary —
        // rejoining both with the redo's empty separator fused « de »+« avoir » into
        // « deavoir » (the proven corruption). Both sides must still tokenize to the
        // same slot count, the parity offline alignment relies on.
        var (c, done) = New();
        c.Word("de", "de", ' ');
        c.Word("d'", "d'", '\'');   // elision commit — empty display separator
        c.Edit("de", "d'");         // fold the retype back into the « de » slot
        c.Word("avoir", "avoir", '.');

        Assert.Equal("de avoir.", done[0].Typed);
        Assert.Equal("d'avoir.", done[0].Final);
        Assert.Equal(
            WordBoundaries.Tokenize(done[0].Typed).Count(),
            WordBoundaries.Tokenize(done[0].Final).Count());
    }
}
