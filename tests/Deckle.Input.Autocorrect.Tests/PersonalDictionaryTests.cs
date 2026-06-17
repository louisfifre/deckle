using Deckle.Input.Autocorrect;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// Comportement du dictionnaire personnel : adoption, décroissance, suppression,
// persistance. Horloge injectée pour rendre la décroissance déterministe (pas
// de Thread.Sleep de 14 jours) ; un fichier temporaire par test, supprimé en
// fin. On n'assert que ce qu'un appelant observe via le contrat IPersonalLexicon
// et l'API de maintenance — jamais la forme exacte du POCO sur le disque.
[Trait("Category", "unit")]
public sealed class PersonalDictionaryTests : IDisposable
{
    private readonly string _path;
    private DateTimeOffset _now = new(2026, 06, 12, 0, 0, 0, TimeSpan.Zero);

    public PersonalDictionaryTests()
    {
        _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"deckle-pdict-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (System.IO.File.Exists(_path)) System.IO.File.Delete(_path);
    }

    private PersonalDictionary New() => new(_path, () => _now);

    [Fact]
    public void ThreeCommitsAdoptTheWord()
    {
        using var dict = New();

        dict.RecordCommit("café");
        dict.RecordCommit("café");
        dict.RecordCommit("café");

        // 3 × boost 1.0 au même instant ⇒ poids 3.0 ⇒ seuil d'adoption atteint.
        Assert.True(dict.IsAdopted("café"));
        Assert.Contains("café", dict.AdoptedWords);
    }

    [Fact]
    public void TwoCommitsDoNotAdopt()
    {
        using var dict = New();

        dict.RecordCommit("café");
        dict.RecordCommit("café");

        Assert.False(dict.IsAdopted("café"));
        Assert.DoesNotContain("café", dict.AdoptedWords);
    }

    [Fact]
    public void WeightHalvesAfterOneHalfLife()
    {
        using var dict = New();

        // Revert ⇒ adopté tout de suite, au-dessus du seuil.
        dict.RecordRevert("café", "cafe");
        Assert.True(dict.IsAdopted("café"));

        // Poids effectif au revert : horloge non avancée (days ≤ 0 ⇒ pas de
        // décroissance), donc c'est le boost shippé, non figé ici.
        var w0 = dict.SnapshotWords().Single(w => w.Word == "café").EffectiveWeight;

        // Après une demi-vie (14 jours), le poids effectif est divisé par deux —
        // l'invariant tient quel que soit le boost.
        _now = _now.AddDays(14);
        Assert.False(dict.IsAdopted("café"));

        var snap = dict.SnapshotWords().Single(w => w.Word == "café");
        Assert.Equal(w0 / 2.0, snap.EffectiveWeight, precision: 3);
        Assert.False(snap.Adopted);
    }

    [Fact]
    public void RevertAdoptionSurvivesTheClockAdvancing()
    {
        using var dict = New();

        dict.RecordRevert("café", "cafe");

        // Le boost (3.5) dépasse le seuil (3.0) : l'adoption tient encore le
        // lendemain — un boost ÉGAL au seuil la perdrait à la première lecture
        // décayée, des millisecondes plus tard.
        _now = _now.AddDays(1);
        Assert.True(dict.IsAdopted("café"));

        // 7 jours au total : 3.5 × 2^(-7/14) ≈ 2.47, sous le seuil.
        _now = _now.AddDays(6);
        Assert.False(dict.IsAdopted("café"));
    }

    [Fact]
    public void RevertSuppressesForeverAndAdoptsTheLiteral()
    {
        using var dict = New();

        dict.RecordRevert("foo", "bar");

        // La paire est supprimée — la correction ne se redéclenche jamais seule.
        Assert.True(dict.IsSuppressed("foo", "bar"));
        // Et le littéral est adopté immédiatement (« mon mot »).
        Assert.True(dict.IsAdopted("foo"));

        // La suppression survit même quand le poids du mot décroît dans le néant.
        _now = _now.AddDays(365);
        Assert.True(dict.IsSuppressed("foo", "bar"));
    }

    [Fact]
    public void RevertIsIdempotentOnTheSuppressionPair()
    {
        using var dict = New();

        dict.RecordRevert("foo", "bar");
        dict.RecordRevert("foo", "bar");

        Assert.Single(dict.SnapshotSuppressions());
    }

    [Fact]
    public void ManualAccentFixBoostsTheFixedForm()
    {
        using var dict = New();

        // 1.5 (manual) + 1.5 (manual) = 3.0 ⇒ adopté ; deux corrections à la main
        // suffisent, là où il faut trois commits.
        dict.RecordManualAccentFix("francais", "français");
        dict.RecordManualAccentFix("francais", "français");

        Assert.True(dict.IsAdopted("français"));
    }

    [Fact]
    public void IsSuppressedIsCaseInsensitive()
    {
        using var dict = New();

        dict.RecordRevert("Foo", "Bar");

        Assert.True(dict.IsSuppressed("foo", "bar"));
        Assert.True(dict.IsSuppressed("FOO", "BAR"));
    }

    [Fact]
    public void CapPruneKeepsTheHeaviest()
    {
        using var dict = New();

        // Cap mots légers (un commit chacun) + un mot lourd à 3.0.
        const string heavy = "héritage";
        dict.RecordCommit(heavy);
        dict.RecordCommit(heavy);
        dict.RecordCommit(heavy);

        for (int i = 0; i < PersonalDictionary.MaxWords; i++)
            dict.RecordCommit($"light{i}");

        // L'insertion au-delà du cap élague le plus léger : le total reste plafonné,
        // le mot lourd survit, et exactement un léger a été évincé.
        var words = dict.SnapshotWords();
        Assert.Equal(PersonalDictionary.MaxWords, words.Count);
        Assert.Contains(words, w => w.Word == heavy);
        Assert.Equal(PersonalDictionary.MaxWords - 1, words.Count(w => w.Word.StartsWith("light")));
    }

    [Fact]
    public void DecayedToDustEntriesAreDroppedOnMutation()
    {
        using var dict = New();

        dict.RecordCommit("éphémère");        // poids 1.0
        Assert.Single(dict.SnapshotWords());

        // Très loin dans le futur : 1.0 décroît bien sous le seuil de poussière
        // (0.05). La poussière est balayée à la prochaine mutation, pas avant.
        _now = _now.AddDays(365);
        dict.RecordCommit("autre");           // déclenche l'élagage

        var words = dict.SnapshotWords();
        Assert.DoesNotContain(words, w => w.Word == "éphémère");
        Assert.Contains(words, w => w.Word == "autre");
    }

    [Fact]
    public void RemoveWordAndRemoveSuppressionReturnWhetherSomethingWent()
    {
        using var dict = New();

        dict.RecordCommit("mot");
        dict.RecordRevert("orig", "repl");

        Assert.True(dict.RemoveWord("mot"));
        Assert.False(dict.RemoveWord("mot"));        // déjà parti
        Assert.True(dict.RemoveSuppression("orig", "repl"));
        Assert.False(dict.RemoveSuppression("orig", "repl"));

        // RemoveWord("mot") n'enlève que "mot" ; "orig", adopté par le revert,
        // reste un mot. RemoveSuppression n'efface que la paire, pas le littéral.
        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "mot");
        Assert.Contains(dict.SnapshotWords(), w => w.Word == "orig");
        Assert.Empty(dict.SnapshotSuppressions());
    }

    [Fact]
    public void PurgeClearsEverything()
    {
        using var dict = New();

        dict.RecordCommit("a");
        dict.RecordRevert("b", "c");

        dict.Purge();

        Assert.Empty(dict.SnapshotWords());
        Assert.Empty(dict.SnapshotSuppressions());
    }

    [Fact]
    public void PersistenceRoundtrip()
    {
        // Première instance : signaux + flush synchrone forcé.
        using (var dict = New())
        {
            dict.RecordCommit("café");
            dict.RecordCommit("café");
            dict.RecordCommit("café");
            dict.RecordRevert("foo", "bar");
            dict.Flush();
        }

        // Seconde instance sur le même fichier : relit l'état au démarrage.
        using var reopened = New();
        Assert.True(reopened.IsAdopted("café"));
        Assert.True(reopened.IsSuppressed("foo", "bar"));
        Assert.True(reopened.IsAdopted("foo"));
    }

    [Fact]
    public void DisposeFlushesToDisk()
    {
        // Sans Flush explicite : Dispose doit suffire à persister (debounce
        // court-circuité). On vérifie via une relecture, pas via le contenu brut.
        using (var dict = New())
        {
            dict.RecordCommit("durable");
            dict.RecordCommit("durable");
            dict.RecordCommit("durable");
        } // Dispose ⇒ Flush

        using var reopened = New();
        Assert.True(reopened.IsAdopted("durable"));
    }
}
