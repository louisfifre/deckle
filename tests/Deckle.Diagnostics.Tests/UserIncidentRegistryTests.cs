using Xunit;

namespace Deckle.Diagnostics.Tests;

// What is open, in which order, and when a listener hears of it. Each test
// builds its own registry, so the process-wide instance stays out of it.
[Trait("Category", "unit")]
public sealed class UserIncidentRegistryTests
{
    [Fact]
    public void AnOpenedIncidentIsInTheSnapshotWithItsDeclaration()
    {
        var registry = new UserIncidentRegistry();
        registry.Declare("mic", "recording", "RecordingPage");

        registry.Open("mic", "probe");

        UserIncident incident = Assert.Single(registry.Snapshot());
        Assert.Equal(new UserIncident("mic", "probe", "recording", "RecordingPage"), incident);
        Assert.True(registry.IsOpen("mic"));
    }

    [Fact]
    public void OpeningTwiceKeepsTheFirstReasonAndNotifiesOnce()
    {
        var registry = new UserIncidentRegistry();
        int changes = 0;
        registry.Changed += () => changes++;

        registry.Open("vad", "model_missing");
        registry.Open("vad", "download_failed");

        Assert.Equal(1, changes);
        Assert.Equal("model_missing", Assert.Single(registry.Snapshot()).Reason);
    }

    [Fact]
    public void ClosingWhatIsNotOpenChangesNothingAndNotifiesNobody()
    {
        var registry = new UserIncidentRegistry();
        int changes = 0;
        registry.Changed += () => changes++;

        registry.Close("vad");
        registry.Open("vad", "model_missing");
        registry.Close("vad");
        registry.Close("vad");

        Assert.Equal(2, changes);
        Assert.Empty(registry.Snapshot());
        Assert.False(registry.IsOpen("vad"));
    }

    [Fact]
    public void TheSnapshotFollowsDeclarationOrderThenOpeningOrder()
    {
        var registry = new UserIncidentRegistry();
        registry.Declare("first", "a", "PageA");
        registry.Declare("second", "b", "PageB");

        registry.Open("loose_late", "x");
        registry.Open("second", "x");
        registry.Open("loose_early", "x");
        registry.Open("first", "x");

        Assert.Equal(
            ["first", "second", "loose_late", "loose_early"],
            registry.Snapshot().Select(i => i.Id));
        Assert.Null(registry.Snapshot()[2].Module);
    }

    [Fact]
    public void ADeclarationArrivingAfterTheOpenDescribesItAndNotifies()
    {
        var registry = new UserIncidentRegistry();
        registry.Open("toasts", "elevated");
        int changes = 0;
        registry.Changed += () => changes++;

        registry.Declare("toasts", "notifications", "GeneralPage");

        Assert.Equal(1, changes);
        Assert.Equal("notifications", Assert.Single(registry.Snapshot()).Module);
    }

    [Fact]
    public void TheSnapshotIsACopy()
    {
        var registry = new UserIncidentRegistry();
        registry.Open("mic", "probe");

        IReadOnlyList<UserIncident> before = registry.Snapshot();
        registry.Close("mic");

        Assert.Single(before);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void ConcurrentOpensAndClosesEndInTheStateOfTheLastCallPerId()
    {
        var registry = new UserIncidentRegistry();
        string[] ids = ["a", "b", "c", "d"];

        Parallel.For(0, 4000, i =>
        {
            string id = ids[i % ids.Length];
            if (i % 2 == 0) registry.Open(id, "x");
            else registry.Close(id);
        });
        foreach (string id in ids) registry.Open(id, "final");

        Assert.Equal(ids.Length, registry.Snapshot().Count);
    }
}
