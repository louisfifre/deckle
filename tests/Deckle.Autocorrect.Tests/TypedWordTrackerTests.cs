using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

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
        public readonly List<bool> DroppedPartials = new();

        public Recorder(TypedWordTracker t)
        {
            t.WordCommitted += c => Commits.Add(c);
            t.WordEdited += e => Edits.Add(e);
            t.TrackerReset += (r, dropped) => { Resets.Add(r); DroppedPartials.Add(dropped); };
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
    public void ThreeWordsChainTheTwoWordContext()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "de la mer ");

        Assert.Equal(3, rec.Commits.Count);
        Assert.Null(rec.Commits[1].PreviousPreviousWord);        // "la": only one word before
        Assert.Equal("la", rec.Commits[2].PreviousWord);         // "mer"
        Assert.Equal("de", rec.Commits[2].PreviousPreviousWord); // two back — the trigram context
    }

    [Fact]
    public void ReopeningRestoresTheTwoWordContext()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "de la mer ");  // commits de, la, mer; edit window on "mer"
        Backspace(t);           // re-open "mer" (eats the boundary)
        Backspace(t, 3);        // erase "mer"
        Type(t, "lac ");        // re-commit a different word in the same slot

        var last = rec.Commits[^1];
        Assert.Equal("lac", last.Word);
        Assert.Equal("la", last.PreviousWord);                   // context restored…
        Assert.Equal("de", last.PreviousPreviousWord);           // …two deep
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

    // Régression injection (JOURNAL 2026-07-02). L'apostrophe d'élision vit
    // DANS le mot (« j' ») et ne s'affiche jamais comme frontière séparée. La
    // 1re backspace après un commit d'élision a déjà rongé cette apostrophe à
    // l'écran ; le buffer réouvert la laisse tomber aussi pour rester collé à
    // l'écran. Retaper l'apostrophe re-valide « j' » à l'identique — aucun
    // WordEdit, car rien n'a changé.
    [Fact]
    public void ElisionReopenDropsTheTrailingApostropheAndRecommitsWithoutEdit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "j");
        Type(t, "'");                      // commit « j' » sur son apostrophe attachée
        Assert.Equal('\'', rec.Commits[0].Boundary);

        Backspace(t);                      // rouvre « j' » MINUS l'apostrophe → « j »
        Assert.Equal("j", t.CurrentWord);

        Type(t, "'");                      // re-valide « j' » à l'identique
        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("j'", rec.Commits[1].Word);
        Assert.Empty(rec.Edits);           // rien n'a changé → aucun WordEdit
    }

    // Réouverture d'élision puis correction : « j' » → « je ». Le buffer rouvre
    // « j », l'utilisateur tape « e » puis une frontière ; le re-commit « je »
    // récolte un WordEdit contre la forme validée d'origine « j' ».
    [Fact]
    public void ElisionReopenThenRetypeHarvestsTheWordEdit()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "j");
        Type(t, "'");        // commit « j' »
        Backspace(t);        // rouvre → « j »
        Type(t, "e");        // « je »
        Type(t, " ");        // re-commit sur frontière

        Assert.Equal(2, rec.Commits.Count);
        Assert.Equal("je", rec.Commits[1].Word);
        var edit = Assert.Single(rec.Edits);
        Assert.Equal("j'", edit.Original);
        Assert.Equal("je", edit.Replacement);
    }

    // Le pendant de la régression : un commit NORMAL (mot + espace) rouvre le
    // mot ENTIER à la 1re backspace — la frontière espace s'affichait bien à
    // l'écran, donc rien n'est rogné. La correction élision ne doit pas avoir
    // déplacé ce comportement.
    [Fact]
    public void NormalCommitReopensTheFullWordOnFirstBackspace()
    {
        var t = new TypedWordTracker();

        Type(t, "mot ");     // frontière espace, affichée
        Backspace(t);        // rouvre « mot » en entier

        Assert.Equal("mot", t.CurrentWord);
    }

    // Après une réouverture d'élision, effacer au-delà du début connu reste un
    // hard reset — comme pour un mot normal.
    [Fact]
    public void ElisionReopenBackspacingPastTheStartResets()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "j");
        Type(t, "'");        // commit « j' »
        Backspace(t);        // rouvre → « j »
        Backspace(t);        // efface « j » → buffer vide réouvert
        Assert.Equal(string.Empty, t.CurrentWord);
        Assert.Empty(rec.Resets);

        Backspace(t);        // une de plus → au-delà du début connu
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
    public void ResetMidWordReportsTheDroppedPartial()
    {
        // Un reset en plein mot jette le préfixe déjà tapé ; le signal dit au
        // consommateur que la suite du mot committera comme un fragment.
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "probl");
        t.NotifyFocusChanged();

        Assert.Equal(new[] { true }, rec.DroppedPartials.ToArray());
    }

    [Fact]
    public void ResetBetweenWordsReportsNoDroppedPartial()
    {
        // Buffer vide au reset : rien n'était en vol, le mot suivant est légitime.
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

        Type(t, new string('a', TypedWordTracker.BufferCap + 1)); // un caractère au-delà du cap déclenche le hard reset

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

    // Le moteur réagit à un commit DEPUIS le handler WordCommitted (réentrance) :
    // il injecte la correction puis appelle ReplaceLastCommitted pour réaligner
    // le tracker sur l'écran. L'état du commit doit donc être posé AVANT que
    // l'événement parte, et rien ne doit écraser le réalignement après.
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

        // Le mot suivant chaîne sur la forme corrigée — celle de l'écran.
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
        Backspace(t); // rouvre le mot tel qu'il est à l'écran : la forme corrigée

        Assert.Equal("français", t.CurrentWord);
    }

    [Fact]
    public void BoundaryOnEmptyBufferClosesTheEditWindow()
    {
        var t = new TypedWordTracker();
        var rec = new Recorder(t);

        Type(t, "mot  ");    // commit, puis un espace surnuméraire
        Backspace(t);        // mange l'espace de bruit — ne rouvre PAS « mot »
        Assert.Equal(string.Empty, t.CurrentWord);

        Type(t, "deux ");
        Assert.Equal(2, rec.Commits.Count);               // aucun commit fantôme
        Assert.Equal("mot", rec.Commits[1].PreviousWord); // la chaîne survit
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
