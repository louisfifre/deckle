using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// TypedWordTracker is the pure state machine that accumulates the word under the
// caret, commits it on a boundary, and harvests the « typed wrong, erased,
// retyped » gesture as a WordEdit. No OS calls: time arrives on keystrokes and
// effects leave as events, so everything is observable keystroke by keystroke.
[Trait("Category", "unit")]
public class TypedWordTrackerTests
{
    private sealed class Recorder
    {
        public readonly List<WordCommit> Commits = new();
        public readonly List<WordEdit> Edits = new();
        public readonly List<ResetReason> Resets = new();
        public readonly List<bool> DroppedPartials = new();

        public Recorder(TypedWordTracker t)
        {
            t.WordCommitted += c => Commits.Add(c);
            t.WordEdited += e => Edits.Add(e);
            t.TrackerReset += (r, dropped) => { Resets.Add(r); DroppedPartials.Add(dropped); };
        }
    }

    private static void Type(TypedWordTracker t, string text, double time = 0)
    {
        foreach (char c in text)
            t.OnKeystroke(new Keystroke(KeystrokeKind.Text, c.ToString(), time));
    }

    private static void Press(TypedWordTracker t, KeystrokeKind kind, double time = 0) =>
        t.OnKeystroke(Keystroke.Of(kind, time));

    private static void Backspace(TypedWordTracker t, int count = 1)
    {
        for (int i = 0; i < count; i++)
            t.OnKeystroke(Keystroke.Of(KeystrokeKind.Backspace, 0));
    }

    [Fact]
    public void WordThenSpaceCommitsWithBoundaryAndNullPrevious()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "bonjour");
        Type(t, " ");

        var c = Assert.Single(rec.Commits);
        Assert.Equal("bonjour", c.Word);
        Assert.Equal(' ', c.Boundary);
        Assert.Null(c.PreviousWord);
    }

    [Fact]
    public void TwoWordsChainPreviousWord()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "le chat ");

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("le", rec.Commits[0].Word);
        Assert.Null(rec.Commits[0].PreviousWord);
        Assert.Equal("chat", rec.Commits[1].Word);
        Assert.Equal("le", rec.Commits[1].PreviousWord);
    }

    [Fact]
    public void ThreeWordsChainTheTwoWordContext()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "de la mer ");

        Assert.Equal(3, rec.Commits.Count);
        Assert.Null(rec.Commits[1].PreviousPreviousWord);
        Assert.Equal("la", rec.Commits[2].PreviousWord);
        Assert.Equal("de", rec.Commits[2].PreviousPreviousWord); // the trigram context
    }

    [Fact]
    public void ReopeningRestoresTheTwoWordContext()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "de la mer ");
        Backspace(t);           // eats the boundary, re-opening "mer"
        Backspace(t, 3);
        Type(t, "lac ");

        var last = rec.Commits[^1];
        Assert.Equal("lac", last.Word);
        Assert.Equal("la", last.PreviousWord);
        Assert.Equal("de", last.PreviousPreviousWord);
    }

    [Fact]
    public void ElisionCommitsPrefixThenChainsTheNextWord()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "l'école ");

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("l'", rec.Commits[0].Word);
        Assert.Equal('\'', rec.Commits[0].Boundary);
        Assert.Null(rec.Commits[0].PreviousWord);
        Assert.Equal("école", rec.Commits[1].Word);
        Assert.Equal("l'", rec.Commits[1].PreviousWord);
    }

    // The elision apostrophe lives INSIDE the word (« j' ») and never shows as a
    // separate boundary, so the first backspace after an elision commit already
    // ate it on screen; the reopened buffer drops it too to stay aligned with the
    // screen.
    [Fact]
    public void ElisionReopenDropsTheTrailingApostropheAndRecommitsWithoutEdit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "j");
        Type(t, "'");
        Assert.Equal('\'', rec.Commits[0].Boundary);

        Backspace(t);
        Assert.Equal("j", t.CurrentWord);

        Type(t, "'");
        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("j'", rec.Commits[1].Word);
        Assert.Empty(rec.Edits);
    }

    [Fact]
    public void ElisionReopenThenRetypeHarvestsTheWordEdit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "j");
        Type(t, "'");
        Backspace(t);
        Type(t, "e");
        Type(t, " ");

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("je", rec.Commits[1].Word);
        var edit = Assert.Single(rec.Edits);
        Assert.Equal("j'", edit.Original);
        Assert.Equal("je", edit.Replacement);
    }

    // A NORMAL commit's space boundary did show on screen, so the first
    // backspace eats only that boundary and reopens the WHOLE word.
    [Fact]
    public void NormalCommitReopensTheFullWordOnFirstBackspace()
    {
        var t = new TypedWordTracker();

        Type(t, "mot ");
        Backspace(t);

        Assert.Equal("mot", t.CurrentWord);
    }

    [Fact]
    public void ElisionReopenBackspacingPastTheStartResets()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "j");
        Type(t, "'");
        Backspace(t);
        Backspace(t);
        Assert.Equal(string.Empty, t.CurrentWord);
        Assert.Empty(rec.Resets);

        Backspace(t);
        Assert.Equal(new[] { ResetReason.Navigation }, rec.Resets.ToArray());
    }

    [Fact]
    public void NonElisionApostropheStaysInOneToken()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "aujourd'hui ");

        var c = Assert.Single(rec.Commits);
        Assert.Equal("aujourd'hui", c.Word);
    }

    [Fact]
    public void TypographicApostropheIsNormalizedInCommittedWord()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "l’an ");   // U+2019

        Assert.Equal("l'", rec.Commits[0].Word); // U+0027 on output
        Assert.Equal("an", rec.Commits[1].Word);
    }

    [Fact]
    public void HyphenatedWordIsOneToken()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "est-ce ");

        var c = Assert.Single(rec.Commits);
        Assert.Equal("est-ce", c.Word);
    }

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData(':')]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData('…')]
    public void PunctuationCommitsAsBoundary(char punct)
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "fin");
        t.OnKeystroke(new Keystroke(KeystrokeKind.Text, punct.ToString(), 0));

        var c = Assert.Single(rec.Commits);
        Assert.Equal("fin", c.Word);
        Assert.Equal(punct, c.Boundary);
    }

    [Fact]
    public void BackspaceInsideLiveWordShortensTheBuffer()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "chein");
        Backspace(t, 2);
        Type(t, "at ");

        var c = Assert.Single(rec.Commits);
        Assert.Equal("cheat", c.Word);
        Assert.Empty(rec.Edits);
    }

    [Fact]
    public void CurrentWordExposesTheLiveBuffer()
    {
        var t = new TypedWordTracker();
        Type(t, "écri");
        Assert.Equal("écri", t.CurrentWord);
        Type(t, " ");
        Assert.Equal(string.Empty, t.CurrentWord);
    }

    // The first backspace eats the boundary and REOPENS the committed word whole;
    // the following ones nibble the word. So « francais » → « français » takes
    // 1 (boundary) + 4 (« cais ») = 5 backspaces, leaving « fran ».
    [Fact]
    public void EditWindowHarvestsCorrectionAsWordEditAndRecommit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "francais");
        Type(t, " ");
        Assert.Single(rec.Commits);

        Backspace(t, 5);
        Assert.Equal("fran", t.CurrentWord);

        Type(t, "çais");
        Type(t, " ", time: 99);

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("français", rec.Commits[1].Word);
        Assert.Equal(99, rec.Commits[1].TimestampMs);

        var edit = Assert.Single(rec.Edits);
        Assert.Equal("francais", edit.Original);
        Assert.Equal("français", edit.Replacement);
    }

    [Fact]
    public void EditWindowRegeneratingSameWordEmitsOnlyTheCommit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "chat ");
        Backspace(t, 2);   // boundary + « t »
        Type(t, "t ");

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("chat", rec.Commits[1].Word);
        Assert.Empty(rec.Edits);
    }

    [Fact]
    public void EditWindowRestoresPreviousWordChainTwoDeep()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "le chat ");
        rec.Commits.Clear();
        rec.Edits.Clear();

        // The re-commit must chain on « le » (two-level memory), not on « chat ».
        Backspace(t, 5);       // boundary + « chat »
        Type(t, "chien ");

        var c = Assert.Single(rec.Commits);
        Assert.Equal("chien", c.Word);
        Assert.Equal("le", c.PreviousWord);
        Assert.Equal(new[] { ("chat", "chien") }, rec.Edits.Select(e => (e.Original, e.Replacement)).ToArray());
    }

    [Fact]
    public void BackspacingPastTheWordStartResetsNavigation()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot ");          // budget = boundary + 3 letters
        Backspace(t, 4);
        Assert.Equal(string.Empty, t.CurrentWord);
        Assert.Empty(rec.Resets);

        Backspace(t);
        Assert.Equal(new[] { ResetReason.Navigation }, rec.Resets.ToArray());
    }

    [Fact]
    public void EnterClearsAllContextSoNextCommitHasNullPrevious()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "premier ");
        Press(t, KeystrokeKind.Enter);
        Type(t, "second ");

        Assert.Equal(ResetReason.Enter, rec.Resets[0]);
        Assert.Equal("second", rec.Commits[1].Word);
        Assert.Null(rec.Commits[1].PreviousWord);
    }

    [Theory]
    [InlineData(KeystrokeKind.Tab, ResetReason.Navigation)]
    [InlineData(KeystrokeKind.Navigation, ResetReason.Navigation)]
    [InlineData(KeystrokeKind.Escape, ResetReason.Escape)]
    [InlineData(KeystrokeKind.Shortcut, ResetReason.Shortcut)]
    [InlineData(KeystrokeKind.Delete, ResetReason.Delete)]
    [InlineData(KeystrokeKind.DeadKey, ResetReason.DeadKey)]
    public void ControlKeystrokesMapToTheirResetReason(KeystrokeKind kind, ResetReason reason)
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "abc");
        Press(t, kind);

        Assert.Equal(new[] { reason }, rec.Resets.ToArray());
        Assert.Equal(string.Empty, t.CurrentWord);
        Assert.Empty(rec.Commits);
    }

    [Fact]
    public void OtherKeystrokeDoesNotReset()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "abc");
        Press(t, KeystrokeKind.Other);

        Assert.Empty(rec.Resets);
        Assert.Equal("abc", t.CurrentWord);
    }

    [Fact]
    public void PointerAndFocusNotificationsReset()
    {
        var t1 = new TypedWordTracker();
        var rec1 = new Recorder(t1);
        Type(t1, "abc");
        t1.NotifyPointerInteraction();
        Assert.Equal(new[] { ResetReason.PointerInteraction }, rec1.Resets.ToArray());
        Assert.Equal(string.Empty, t1.CurrentWord);

        var t2 = new TypedWordTracker();
        var rec2 = new Recorder(t2);
        Type(t2, "abc");
        t2.NotifyFocusChanged();
        Assert.Equal(new[] { ResetReason.FocusChanged }, rec2.Resets.ToArray());
    }

    [Fact]
    public void ResetMidWordReportsTheDroppedPartial()
    {
        // A reset mid-word throws the typed prefix away; the signal tells the
        // consumer that the rest of the word will commit as a fragment.
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "probl");
        t.NotifyFocusChanged();

        Assert.Equal(new[] { true }, rec.DroppedPartials.ToArray());
    }

    [Fact]
    public void ResetBetweenWordsReportsNoDroppedPartial()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot ");
        t.NotifyFocusChanged();

        Assert.Equal(new[] { false }, rec.DroppedPartials.ToArray());
    }

    [Fact]
    public void BufferCapOverflowHardResetsBufferLimit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, new string('a', TypedWordTracker.BufferCap + 1));

        Assert.Equal(new[] { ResetReason.BufferLimit }, rec.Resets.ToArray());
        Assert.Equal(string.Empty, t.CurrentWord);
        Assert.Empty(rec.Commits);
    }

    [Fact]
    public void ConsecutiveBoundariesDoNotClearPreviousWord()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot");
        Type(t, "   ");
        Type(t, "deux ");

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("mot", rec.Commits[0].Word);
        Assert.Equal("deux", rec.Commits[1].Word);
        Assert.Equal("mot", rec.Commits[1].PreviousWord);
    }

    // The engine reacts to a commit FROM the WordCommitted handler (reentrancy):
    // it injects the correction then calls ReplaceLastCommitted to realign the
    // tracker with the screen. The commit state must be set BEFORE the event
    // fires, and nothing may overwrite the realignment afterwards.
    [Fact]
    public void ReplaceLastCommittedFromTheCommitHandlerRealignsTheChain()
    {
        var t = new TypedWordTracker();
        var commits = new List<WordCommit>();
        t.WordCommitted += c =>
        {
            commits.Add(c);
            if (c.Word == "francais")
                t.ReplaceLastCommitted("français");
        };

        Type(t, "francais ");
        Type(t, "ecole ");

        Assert.Equal(2, commits.Count);
        Assert.Equal("français", commits[1].PreviousWord);
    }

    [Fact]
    public void ReplaceLastCommittedFromTheCommitHandlerReopensTheReplacement()
    {
        var t = new TypedWordTracker();
        t.WordCommitted += c =>
        {
            if (c.Word == "francais")
                t.ReplaceLastCommitted("français");
        };

        Type(t, "francais ");
        Backspace(t);

        Assert.Equal("français", t.CurrentWord);
    }

    [Fact]
    public void BoundaryOnEmptyBufferClosesTheEditWindow()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot  ");
        Backspace(t);        // eats the noise space; does NOT reopen « mot »
        Assert.Equal(string.Empty, t.CurrentWord);

        Type(t, "deux ");
        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("mot", rec.Commits[1].PreviousWord);
        Assert.Empty(rec.Edits);
    }

    [Fact]
    public void CommitTimestampPassesThrough()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot");
        t.OnKeystroke(new Keystroke(KeystrokeKind.Text, " ", 4242.0));

        Assert.Equal(4242.0, Assert.Single(rec.Commits).TimestampMs);
    }

    // Parity with WordBoundaries.Tokenize: the tracker must split like the
    // canonical tokenizer, elision « l' » included.
    [Fact]
    public void TrackerCommitsMatchCanonicalTokenization()
    {
        const string sentence = "l'école est-ce aujourd'hui ";
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, sentence);

        var committed = rec.Commits.Select(c => c.Word).ToArray();
        var tokenized = WordBoundaries.Tokenize(sentence).ToArray();
        Assert.Equal(tokenized, committed);
    }

    // ── The separator run (PrecedingSeparators) ──
    // The characters on screen between the previous word and the committing one,
    // the material of the boundary mistouch families. Faithful while nothing
    // disturbed it; empty (unknown) otherwise, and the families abstain.

    [Fact]
    public void PrecedingSeparatorsCarryTheGluedBoundary()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "qu;il ");

        Assert.Equal("", rec.Commits[0].PrecedingSeparators);   // start of the run: unknown
        Assert.Equal(";", rec.Commits[1].PrecedingSeparators);
    }

    [Fact]
    public void PrecedingSeparatorsAccumulateNoiseBoundaries()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "fait, beau ");

        Assert.Equal(", ", rec.Commits[1].PrecedingSeparators);
    }

    [Fact]
    public void PrecedingSeparatorsRenderTheElisionBoundaryEmpty()
    {
        // The elision apostrophe lives INSIDE the word (« l' »): nothing on screen
        // separates it from the next word.
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "l'école ");

        Assert.Equal("", rec.Commits[1].PrecedingSeparators);
    }

    [Fact]
    public void PrecedingSeparatorsGoUnknownWhenBackspaceEatsTheRun()
    {
        // A Backspace on an empty buffer eats a separator: the run no longer
        // matches the screen, so it invalidates itself rather than lie.
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "fait, ");
        Backspace(t);
        Type(t, " beau ");

        Assert.Equal("", rec.Commits[^1].PrecedingSeparators);
    }

    [Fact]
    public void PrecedingSeparatorsGoUnknownOnReopenedWords()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "un mot ");
        Backspace(t, 4); // reopens « mot » and erases it
        Type(t, "sot ");

        WordCommit recommit = rec.Commits[^1];
        Assert.True(recommit.Reopened);
        Assert.Equal("", recommit.PrecedingSeparators);
    }

    [Fact]
    public void PrecedingSeparatorsResetWithTheTracker()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot,");
        Press(t, KeystrokeKind.Enter);
        Type(t, "suite ");

        Assert.Equal("", rec.Commits[^1].PrecedingSeparators);
    }
}
