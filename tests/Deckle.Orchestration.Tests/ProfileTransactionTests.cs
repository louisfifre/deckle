using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class ProfileTransactionTests
{
    private static readonly AppEntry Entry = new() { DisplayName = "Hollow", ProcessName = "Hollow" };

    [Fact]
    public void EffectsApplyInOrderAndRestoreInReverse()
    {
        var journal = new List<string>();
        var effects = new[] { new FakeEffect("a", journal), new FakeEffect("b", journal) };

        AppliedProfile profile = ProfileTransaction.Apply(Entry, effects);
        profile.Restore();

        Assert.Equal(["apply a", "apply b", "restore b", "restore a"], journal);
        Assert.Equal(["a", "b"], profile.Effects);
    }

    [Fact]
    public void AFailingApplyRollsBackTheEarlierEffectsAndLeavesNoRule()
    {
        var journal = new List<string>();
        var effects = new[]
        {
            new FakeEffect("a", journal),
            new FakeEffect("b", journal),
            new FakeEffect("c", journal) { ApplyThrows = true },
        };

        AppliedProfile profile = ProfileTransaction.Apply(Entry, effects);

        Assert.Equal(["apply a", "apply b", "apply c", "restore b", "restore a"], journal);
        Assert.Equal(0, profile.Changes);
        Assert.Empty(profile.Effects);

        profile.Restore();
        Assert.Equal(5, journal.Count);
    }

    [Fact]
    public void ARestoreThatThrowsDoesNotStopTheOthers()
    {
        var journal = new List<string>();
        var effects = new[]
        {
            new FakeEffect("a", journal),
            new FakeEffect("b", journal) { RestoreThrows = true },
            new FakeEffect("c", journal),
        };

        ProfileTransaction.Apply(Entry, effects).Restore();

        Assert.Equal(["apply a", "apply b", "apply c", "restore c", "restore b", "restore a"], journal);
    }

    [Fact]
    public void RestoringTwiceUndoesOnce()
    {
        var journal = new List<string>();
        AppliedProfile profile = ProfileTransaction.Apply(Entry, [new FakeEffect("a", journal)]);

        profile.Restore();
        profile.Restore();

        Assert.Equal(["apply a", "restore a"], journal);
    }

    [Fact]
    public void AnEffectThatDoesNotApplyIsSkipped()
    {
        var journal = new List<string>();
        var effects = new[] { new FakeEffect("a", journal) { Applies = false }, new FakeEffect("b", journal) };

        AppliedProfile profile = ProfileTransaction.Apply(Entry, effects);

        Assert.Equal(["apply b"], journal);
        Assert.Equal(["b"], profile.Effects);
    }

    [Fact]
    public void AnEffectWithNothingToChangeLeavesNothingToRestore()
    {
        var journal = new List<string>();
        AppliedProfile profile = ProfileTransaction.Apply(
            Entry, [new FakeEffect("a", journal) { NothingToChange = true }]);

        profile.Restore();

        Assert.Equal(["apply a"], journal);
        Assert.Equal(0, profile.Changes);
    }

    // Records every apply and restore in a shared journal, so the order across
    // effects is observable.
    internal sealed class FakeEffect(string name, List<string> journal) : IRuleEffect
    {
        public bool Applies { get; init; } = true;

        public bool ApplyThrows { get; init; }

        public bool RestoreThrows { get; init; }

        public bool NothingToChange { get; init; }

        public string Name => name;

        public bool AppliesTo(AppEntry entry) => Applies;

        public Action? Apply(AppEntry entry)
        {
            journal.Add($"apply {name}");
            if (ApplyThrows) throw new InvalidOperationException($"{name} failed");
            if (NothingToChange) return null;
            return () =>
            {
                journal.Add($"restore {name}");
                if (RestoreThrows) throw new InvalidOperationException($"{name} restore failed");
            };
        }
    }
}
