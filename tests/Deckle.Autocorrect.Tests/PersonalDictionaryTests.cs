using System.Text.Json;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

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

        // Trois commits au même instant ⇒ poids 3.0, au-dessus du seuil.
        dict.RecordCommit("café");
        dict.RecordCommit("café");
        dict.RecordCommit("café");
        Assert.True(dict.IsAdopted("café"));

        // Poids effectif de départ : horloge non avancée (days ≤ 0 ⇒ pas de
        // décroissance), donc c'est le poids accumulé, non figé en dur ici.
        var w0 = dict.SnapshotWords().Single(w => w.Word == "café").EffectiveWeight;

        // Après une demi-vie (14 jours), le poids effectif est divisé par deux —
        // l'invariant de décroissance tient quel que soit le poids de départ.
        _now = _now.AddDays(14);
        Assert.False(dict.IsAdopted("café"));

        var snap = dict.SnapshotWords().Single(w => w.Word == "café");
        Assert.Equal(w0 / 2.0, snap.EffectiveWeight, precision: 3);
        Assert.False(snap.Adopted);
    }

    [Fact]
    public void SuppressForeverDoesNotAdoptTheLiteral()
    {
        using var dict = New();

        dict.RecordSuppression("foo", "bar");

        // La paire est supprimée — la correction ne se redéclenche jamais seule.
        Assert.True(dict.IsSuppressed("foo", "bar"));
        // Mais la suppression n'adopte RIEN : le littéral n'entre pas au
        // vocabulaire de son seul fait (aucun boost ne l'accompagne).
        Assert.False(dict.IsAdopted("foo"));
        Assert.Empty(dict.SnapshotWords());

        // La suppression est permanente : elle survit à un temps arbitraire.
        _now = _now.AddDays(365);
        Assert.True(dict.IsSuppressed("foo", "bar"));
    }

    [Fact]
    public void SuppressIsIdempotentOnThePair()
    {
        using var dict = New();

        dict.RecordSuppression("foo", "bar");
        dict.RecordSuppression("foo", "bar");

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

        dict.RecordSuppression("Foo", "Bar");

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
        dict.RecordSuppression("orig", "repl");

        Assert.True(dict.RemoveWord("mot"));
        Assert.False(dict.RemoveWord("mot"));        // déjà parti
        Assert.True(dict.RemoveSuppression("orig", "repl"));
        Assert.False(dict.RemoveSuppression("orig", "repl"));

        // Les deux stores sont indépendants : RemoveWord n'a touché que "mot",
        // RemoveSuppression n'a touché que la paire.
        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "mot");
        Assert.Empty(dict.SnapshotSuppressions());
    }

    [Fact]
    public void PurgeClearsEverything()
    {
        using var dict = New();

        dict.RecordCommit("a");
        dict.RecordSuppression("b", "c");

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
            dict.RecordSuppression("foo", "bar");
            dict.Flush();
        }

        // Seconde instance sur le même fichier : relit l'état au démarrage.
        using var reopened = New();
        Assert.True(reopened.IsAdopted("café"));
        Assert.True(reopened.IsSuppressed("foo", "bar"));
        // La suppression a bien traversé le disque sans emporter d'adoption :
        // "foo" n'est pas un mot pour autant.
        Assert.False(reopened.IsAdopted("foo"));
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

    // ── Migration : la purge des stores empoisonnés ─────────────────────────────

    [Fact]
    public void LegacyPreStampFileIsPurgedAndStampedOnLoad()
    {
        // Fixture legacy : mots et suppressions, AUCUNE clé schemaVersion — c'est
        // l'absence de la clé qui EST le contrat de la génération pré-tampon, donc
        // le seul JSON écrit à la main ici. Le mot pèse 3.0 : il serait adopté
        // s'il survivait, ce qui rend la purge observable.
        System.IO.File.WriteAllText(_path, """
            {
              "words": [
                { "word": "café", "weight": 3.0, "lastSeenUtc": "2026-06-12T00:00:00+00:00" }
              ],
              "suppressions": [
                { "original": "cable", "replacement": "câble", "createdUtc": "2026-06-12T00:00:00+00:00" }
              ]
            }
            """);

        using var dict = New();

        // La version chargée (0) est antérieure au tampon ⇒ les deux stores sont
        // vidés d'un bloc à la première lecture.
        Assert.Empty(dict.SnapshotWords());
        Assert.Empty(dict.SnapshotSuppressions());

        // Et le fichier réécrit porte désormais la version courante — la migration
        // ne se rejouera pas au prochain démarrage. On relit le disque à travers
        // le même POCO plutôt que d'asserter une chaîne brute.
        dict.Flush();
        var onDisk = JsonSerializer.Deserialize<PersonalDictionaryData>(
            System.IO.File.ReadAllText(_path),
            CamelCase);
        Assert.NotNull(onDisk);
        Assert.Equal(PersonalDictionaryData.CurrentSchemaVersion, onDisk!.SchemaVersion);
    }

    [Fact]
    public void CurrentVersionFileIsNotRePurged()
    {
        // Fixture déjà tamponnée à la version courante, avec un mot appris. Écrite
        // via le POCO pour coller exactement à la sérialisation du store.
        var stamped = new PersonalDictionaryData { SchemaVersion = PersonalDictionaryData.CurrentSchemaVersion };
        stamped.Words.Add(new WordEntry { Word = "café", Weight = 3.0, LastSeenUtc = _now });
        System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(stamped, CamelCase));

        using var dict = New();

        // Version chargée == version courante ⇒ pas de purge : le mot survit.
        Assert.True(dict.IsAdopted("café"));
        Assert.Contains(dict.SnapshotWords(), w => w.Word == "café");
    }

    [Fact]
    public void CorruptFileFallsBackStampedSoLaterLearningSurvives()
    {
        // Fichier illisible : le store tombe proprement sur des défauts. Le hook
        // post-load tamponne CE fallback (le correctif JsonSettingsStore), sinon
        // il ressemblerait à un fichier pré-tampon et serait re-purgé au lancement
        // suivant.
        System.IO.File.WriteAllText(_path, "{ this is not valid json ");

        using (var dict = New())
        {
            dict.RecordCommit("café");
            dict.RecordCommit("café");
            dict.RecordCommit("café");
            dict.Flush();
        }

        // Troisième construction sur le fichier réécrit après le fallback : si le
        // fallback avait gardé la version 0, cette lecture le re-migrerait et
        // effacerait le mot. Il survit ⇒ le fallback était bien tamponné.
        using var third = New();
        Assert.True(third.IsAdopted("café"));
    }

    // camelCase — la même politique de nommage que le store, pour lire/écrire les
    // fixtures de migration au format exact du fichier sur disque.
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
