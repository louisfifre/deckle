using Deckle.Anytype.Schema;
using Xunit;

namespace Deckle.Anytype.Tests;

// Pure unit tests on the frozen DevSpace schema map. No I/O: these assert the
// vocabulary lookups the gestures rely on to translate user-facing names and the
// space's (sometimes malformed) wire keys. The three trap keys — misspelled,
// truncated, misleading — are deliberately exercised so a future "cleanup" that
// silently fixes them is caught here, since fixing them would break the live space.
[Trait("Category", "unit")]
public class DevSpaceTests
{
    // ── Priority round-trip ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PriorityKeyForLevelForRoundTripsEveryLevel(int level)
    {
        // KeyFor(level) → wire key → LevelFor → level, for all six levels.
        // Covers the opaque-id levels (0 and 4) alongside the bare-integer ones.
        string key = DevSpace.Priority.KeyFor(level);

        Assert.Equal(level, DevSpace.Priority.LevelFor(key));
    }

    [Fact]
    public void PriorityKeyForZeroAndFourAreTheOpaqueIds()
    {
        // The space stores levels 0 and 4 as opaque option ids, not "0"/"4".
        // Pin them so a transcription drift surfaces immediately.
        Assert.Equal("67cc1782341c16068836b71e", DevSpace.Priority.KeyFor(0));
        Assert.Equal("67c6d722341c1628147d7b1e", DevSpace.Priority.KeyFor(4));
    }

    [Fact]
    public void PriorityLevelForUnknownKeyIsNull()
    {
        Assert.Null(DevSpace.Priority.LevelFor("not-a-priority-key"));
    }

    // ── Vocabulary Resolve ────────────────────────────────────────────────────

    [Fact]
    public void EtatResolveAcceptsExactKey()
    {
        Assert.Equal("ouvert", DevSpace.Etat.Resolve("ouvert"));
    }

    [Fact]
    public void TypeDeTacheResolveAcceptsCaseInsensitiveDisplayName()
    {
        // "Produire" is the French display label of the production key; matching
        // is case-insensitive on the name.
        Assert.Equal("production", DevSpace.TypeDeTache.Resolve("Produire"));
        Assert.Equal("production", DevSpace.TypeDeTache.Resolve("produire"));
    }

    [Fact]
    public void PriorityResolveAcceptsTheIntegerString()
    {
        // For Priority the user can pass the bare level digit; "2" maps to the
        // level-2 wire key (which happens to also be the string "2").
        Assert.Equal("2", DevSpace.Priority.Resolve("2"));
    }

    // ── ResolveTag on closed vs open vocabularies ─────────────────────────────

    [Fact]
    public void ResolveTagOnClosedVocabularyThrowsListingValidOptions()
    {
        // A closed select (état) rejects an unknown value with an ArgumentException
        // whose message enumerates the valid options — that text is the
        // model-facing affordance, so assert the options are present.
        var ex = Assert.Throws<ArgumentException>(
            () => DevSpace.ResolveTag(DevSpace.Props.Etat, "inexistant"));

        Assert.Contains("Terminé", ex.Message);
        Assert.Contains("ouvert", ex.Message);
    }

    [Fact]
    public void ResolveTagOnFreeVocabularyThrowsRatherThanPassingThrough()
    {
        // « tag » is a free multi_select with no frozen vocabulary. ResolveTag must
        // NOT pass an arbitrary value through (the old hole that could let the API
        // auto-create an option): reaching it for a free vocabulary is a routing
        // error, so it throws. The gesture resolves such properties live instead.
        Assert.Throws<InvalidOperationException>(
            () => DevSpace.ResolveTag(DevSpace.Props.Tag, "anything-goes"));
    }

    [Fact]
    public void HasFrozenVocabularyDistinguishesClosedFromFreeProperties()
    {
        // The fork the gesture branches on: closed vocabularies resolve in memory,
        // free ones go to the live resolver.
        Assert.True(DevSpace.HasFrozenVocabulary(DevSpace.Props.Etat));
        Assert.True(DevSpace.HasFrozenVocabulary(DevSpace.Props.TypeDeTache));
        Assert.False(DevSpace.HasFrozenVocabulary(DevSpace.Props.Tag));
    }

    [Fact]
    public void ResolveTagOnClosedVocabularyResolvesADisplayName()
    {
        // The success path of a closed vocabulary: a display name resolves to its
        // wire key.
        Assert.Equal("production", DevSpace.ResolveTag(DevSpace.Props.TypeDeTache, "Produire"));
    }

    // ── TryResolveProperty: label, raw key, and the trap keys ─────────────────

    [Fact]
    public void TryResolvePropertyResolvesByDisplayLabel()
    {
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Task, "État", out string key, out string format);

        Assert.True(ok);
        Assert.Equal(DevSpace.Props.Etat, key);
        Assert.Equal("select", format);
    }

    [Fact]
    public void TryResolvePropertyResolvesByRawKey()
    {
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Task, "etat", out string key, out string format);

        Assert.True(ok);
        Assert.Equal("etat", key);
        Assert.Equal("select", format);
    }

    [Fact]
    public void TryResolvePropertyResolvesTheMisleadingChargeReelleLabel()
    {
        // Trap: « Charge réelle » carries the misleading wire key
        // "charge_estimee_(jours)" (it reads like an estimate but is the real
        // charge). The label must map to that exact ugly key.
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Project, "Charge réelle", out string key, out _);

        Assert.True(ok);
        Assert.Equal("charge_estimee_(jours)", key);
    }

    [Fact]
    public void TryResolvePropertyResolvesTheMalformedTachesLieesLabel()
    {
        // Trap: « Tâche(s) liée(s) » carries the malformed wire key
        // "tache(s)_liee(s)" (no accents). Reports anchor to tasks through this
        // exact key, so the malformed form must survive.
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Rapport, "Tâche(s) liée(s)", out string key, out _);

        Assert.True(ok);
        Assert.Equal("tache(s)_liee(s)", key);
    }

    [Fact]
    public void TryResolvePropertyResolvesTrapKeysVerbatim()
    {
        // The trap keys resolve to themselves when passed as raw keys — the code
        // speaks the wire, it does not normalize.
        Assert.True(DevSpace.TryResolveProperty(
            DevSpace.Types.Project, "charge_estimee_(jours)", out string charge, out _));
        Assert.Equal("charge_estimee_(jours)", charge);

        Assert.True(DevSpace.TryResolveProperty(
            DevSpace.Types.Rapport, "tache(s)_liee(s)", out string taches, out _));
        Assert.Equal("tache(s)_liee(s)", taches);
    }

    [Fact]
    public void TryResolvePropertyReturnsFalseForUnknownNameOrKey()
    {
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Task, "no such property", out string key, out string format);

        Assert.False(ok);
        Assert.Equal("", key);
        Assert.Equal("", format);
    }
}
