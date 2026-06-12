using Deckle.Input.Autocorrect.Tracking;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// Tests de comportement sur TypedWordTracker — la machine à états pure qui
// accumule le mot sous le caret, le valide sur une frontière, et récolte le
// geste « tapé faux, effacé, retapé » en WordEdit. Aucun appel OS : le temps
// arrive sur les keystrokes, les effets sortent en événements, donc tout est
// observable frappe par frappe. Les cas sont réalistes en français : élision,
// trait d'union, ponctuation, et le scénario complet de fenêtre d'édition.
[Trait("Category", "unit")]
public class TypedWordTrackerTests
{
    // Collecteur des trois flux d'événements, monté sur un tracker.
    private sealed class Recorder
    {
        public readonly List<WordCommit> Commits = new();
        public readonly List<WordEdit> Edits = new();
        public readonly List<ResetReason> Resets = new();

        public Recorder(TypedWordTracker t)
        {
            t.WordCommitted += c => Commits.Add(c);
            t.WordEdited += e => Edits.Add(e);
            t.TrackerReset += r => Resets.Add(r);
        }
    }

    // Frappe une chaîne caractère par caractère (chaque char = un Keystroke Text).
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
    public void ElisionCommitsPrefixThenChainsTheNextWord()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        // « l'école » : l'apostrophe ferme « l' », puis « école » se valide
        // sur la frontière suivante avec PreviousWord « l' ».
        Type(t, "l'école ");

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("l'", rec.Commits[0].Word);
        Assert.Equal('\'', rec.Commits[0].Boundary);
        Assert.Null(rec.Commits[0].PreviousWord);
        Assert.Equal("école", rec.Commits[1].Word);
        Assert.Equal("l'", rec.Commits[1].PreviousWord);
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

        Assert.Equal("l'", rec.Commits[0].Word); // U+0027 en sortie
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
        Backspace(t, 2);   // « chein » → « che »
        Type(t, "at ");    // « cheat »? non : « che » + « at » = « cheat »

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

    // Scénario complet de fenêtre d'édition. Spec : la 1re backspace mange la
    // frontière et RÉOUVRE le mot validé en entier ; les suivantes grignotent
    // le mot. Donc pour passer de « francais » à « français » (remplacer le
    // « cais » final par « çais »), il faut 1 (frontière) + 4 (les 4 lettres
    // « cais ») = 5 backspaces, ce qui laisse « fran ». La spec écrit « ×4 » de
    // façon approximative ; le modèle exact est suivi ici (déviation notée).
    [Fact]
    public void EditWindowHarvestsCorrectionAsWordEditAndRecommit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "francais");
        Type(t, " ");
        Assert.Single(rec.Commits);

        Backspace(t, 5);             // frontière + « cais » → buffer « fran »
        Assert.Equal("fran", t.CurrentWord);

        Type(t, "çais");             // « fran » + « çais » = « français »
        Type(t, " ", time: 99);

        // Deux commits au total (le premier « francais », le second « français »).
        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("français", rec.Commits[1].Word);
        Assert.Equal(99, rec.Commits[1].TimestampMs);

        // ET un WordEdit récolté.
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
        Backspace(t, 2);   // frontière + « t » → « cha »
        Type(t, "t ");     // retape « chat » à l'identique

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("chat", rec.Commits[1].Word);
        Assert.Empty(rec.Edits); // identique → aucun WordEdit
    }

    [Fact]
    public void EditWindowRestoresPreviousWordChainTwoDeep()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "le chat ");   // commits: le, chat ; previousWord(chat)=le
        rec.Commits.Clear();
        rec.Edits.Clear();

        // Réouvre « chat », corrige en « chien » : le re-commit doit chaîner
        // sur « le » (mémoire deux niveaux), pas sur « chat ».
        Backspace(t, 5);       // frontière + « chat » → buffer vide réouvert
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

        Type(t, "mot ");          // commit « mot » (len 3), budget = frontière + 3
        Backspace(t, 4);          // mange frontière + « mot » → buffer vide réouvert
        Assert.Equal(string.Empty, t.CurrentWord);
        Assert.Empty(rec.Resets);

        Backspace(t);             // une de plus → au-delà du début connu
        Assert.Equal(new[] { ResetReason.Navigation }, rec.Resets.ToArray());
    }

    [Fact]
    public void EnterClearsAllContextSoNextCommitHasNullPrevious()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "premier ");      // commit, previousWord = premier
        Press(t, KeystrokeKind.Enter);
        Type(t, "second ");

        Assert.Equal(ResetReason.Enter, rec.Resets[0]);
        // Le commit d'après Enter n'a pas de PreviousWord.
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
        Assert.Equal(string.Empty, t.CurrentWord); // buffer vidé
        Assert.Empty(rec.Commits);                  // pas de commit sur reset
    }

    [Fact]
    public void OtherKeystrokeDoesNotReset()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "abc");
        Press(t, KeystrokeKind.Other);

        Assert.Empty(rec.Resets);
        Assert.Equal("abc", t.CurrentWord); // le buffer survit
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
    public void BufferCapOverflowHardResetsBufferLimit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, new string('a', 65)); // dépasse le cap de 64

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
        Type(t, "   ");  // une frontière puis deux espaces de bruit
        Type(t, "deux ");

        // « mot » et « deux » seulement ; les frontières surnuméraires sont du
        // bruit et ne cassent pas la chaîne.
        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("mot", rec.Commits[0].Word);
        Assert.Equal("deux", rec.Commits[1].Word);
        Assert.Equal("mot", rec.Commits[1].PreviousWord);
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

    // Parité avec WordBoundaries.Tokenize : le tracker découpe comme le
    // tokeniseur canonique. On exerce une phrase et on compare la suite des
    // mots validés à la tokenisation, hors apostrophe d'élision (le tracker
    // valide « l' » comme token, ce que Tokenize fait aussi).
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
}
