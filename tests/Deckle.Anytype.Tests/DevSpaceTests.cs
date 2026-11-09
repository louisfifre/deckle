using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Pure unit tests on the frozen DevSpace schema map. No I/O: these assert the
// vocabulary lookups the gestures rely on to translate user-facing names and the
// space's wire keys. That DevSpace says what the manifest says is held by
// PmSchemaManifestTests.
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
        // Covers the opaque-id levels (0 and 4) alongside the bare-integer ones.
        string key = DevSpace.Priority.KeyFor(level);

        Assert.Equal(level, DevSpace.Priority.LevelFor(key));
    }

    [Fact]
    public void PriorityKeysArePAndTheLevel()
    {
        Assert.Equal("p0", DevSpace.Priority.KeyFor(0));
        Assert.Equal("p5", DevSpace.Priority.KeyFor(5));
        Assert.Equal(3, DevSpace.Priority.LevelFor("p3"));
    }

    [Fact]
    public void PriorityLevelForUnknownKeyIsNull()
    {
        Assert.Null(DevSpace.Priority.LevelFor("not-a-priority-key"));
    }

    // ── Vocabulary Resolve ────────────────────────────────────────────────────

    [Fact]
    public void StatusResolveAcceptsExactKeyAndDisplayName()
    {
        Assert.Equal("doing", DevSpace.Status.Resolve("doing"));
        Assert.Equal("paused", DevSpace.Status.Resolve("en pause"));
        Assert.Equal("ready", DevSpace.Status.Resolve("Prêt"));
    }

    [Theory]
    [InlineData("termine")]
    [InlineData("abandonne")]
    [InlineData("ouvert")]
    [InlineData("en_attente")]
    public void StatusRefusesTheStatesOfTheV5Space(string old)
    {
        // A finished or abandoned project is archived, not a state; the other
        // old keys were renamed.
        Assert.Null(DevSpace.Status.Resolve(old));
    }

    [Fact]
    public void TaskTypeResolveAcceptsCaseInsensitiveDisplayName()
    {
        Assert.Equal("production", DevSpace.TaskType.Resolve("Produire"));
        Assert.Equal("production", DevSpace.TaskType.Resolve("produire"));
        Assert.Equal("research", DevSpace.TaskType.Resolve("Chercher"));
        Assert.Null(DevSpace.TaskType.Resolve("recherche"));
    }

    [Fact]
    public void PriorityResolveAcceptsTheIntegerString()
    {
        // For Priority the user can pass the bare level digit; "2" maps to the
        // level-2 wire key.
        Assert.Equal("p2", DevSpace.Priority.Resolve("2"));
        Assert.Equal("p2", DevSpace.Priority.Resolve("p2"));
    }

    // ── ResolveTag on closed vs open vocabularies ─────────────────────────────

    [Fact]
    public void ResolveTagOnClosedVocabularyThrowsListingValidOptions()
    {
        // The listed options are the model-facing affordance.
        var ex = Assert.Throws<ArgumentException>(
            () => DevSpace.ResolveTag(DevSpace.Props.Status, "inexistant"));

        Assert.Contains("En pause", ex.Message);
        Assert.Contains("paused", ex.Message);
    }

    [Fact]
    public void ResolveTagOnFreeVocabularyThrowsRatherThanPassingThrough()
    {
        // « tag » is a free multi_select with no frozen vocabulary. Passing an
        // arbitrary value through could let the API auto-create an option, so
        // reaching ResolveTag for it is a routing error.
        Assert.Throws<InvalidOperationException>(
            () => DevSpace.ResolveTag(DevSpace.Props.Tag, "anything-goes"));
    }

    [Fact]
    public void HasFrozenVocabularyDistinguishesClosedFromFreeProperties()
    {
        Assert.True(DevSpace.HasFrozenVocabulary(DevSpace.Props.Status));
        Assert.True(DevSpace.HasFrozenVocabulary(DevSpace.Props.TaskType));
        Assert.False(DevSpace.HasFrozenVocabulary(DevSpace.Props.Tag));
    }

    [Fact]
    public void ResolveTagOnClosedVocabularyResolvesADisplayName()
    {
        Assert.Equal("production", DevSpace.ResolveTag(DevSpace.Props.TaskType, "Produire"));
    }

    // ── TryResolveProperty: label and raw key ─────────────────────────────────

    [Fact]
    public void TryResolvePropertyResolvesByDisplayLabel()
    {
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Task, "État", out string key, out string format);

        Assert.True(ok);
        Assert.Equal(DevSpace.Props.Status, key);
        Assert.Equal("select", format);
    }

    [Fact]
    public void TryResolvePropertyResolvesByRawKey()
    {
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Task, "status", out string key, out string format);

        Assert.True(ok);
        Assert.Equal("status", key);
        Assert.Equal("select", format);
    }

    [Fact]
    public void ChargeAndTimeAreOnePropertyPerType()
    {
        // A project measures effort; a task measures time. Same label family,
        // two keys, each on the types that use it.
        Assert.True(DevSpace.TryResolveProperty(DevSpace.Types.Project, "Charge réelle", out string effort, out _));
        Assert.Equal("effort_actual", effort);

        Assert.True(DevSpace.TryResolveProperty(DevSpace.Types.Task, "Temps réel", out string time, out _));
        Assert.Equal("time_actual", time);
        Assert.False(DevSpace.TryResolveProperty(DevSpace.Types.Task, "effort_actual", out _, out _));
    }

    [Fact]
    public void AProjectHasNoDoneBox()
    {
        Assert.False(DevSpace.TryResolveProperty(DevSpace.Types.Project, "done", out _, out _));
        Assert.True(DevSpace.TryResolveProperty(DevSpace.Types.Task, "done", out string key, out _));
        Assert.Equal("done", key);
    }

    [Fact]
    public void TryResolvePropertyResolvesTheReportTasksLabel()
    {
        bool ok = DevSpace.TryResolveProperty(
            DevSpace.Types.Report, "Tâches", out string key, out _);

        Assert.True(ok);
        Assert.Equal("tasks", key);
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
