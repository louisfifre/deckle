using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The tray shows lights that stopped following the screen, from the push
// incident's opening to its recovery or to the end of the run. The collection
// runs alone, so the process-wide registry sees only these tests.
[Trait("Category", "observability")]
[Collection(AmbientObservabilityCollection.Name)]
public sealed class AmbientUserIncidentTests
{
    private sealed class Host : IAmbientEngineHost
    {
        public AmbientSettings Ambient { get; } = new();
    }

    private static bool IsOpen => UserIncidentRegistry.Instance.IsOpen(UserIncidents.HuePush);

    private static void FailPushes(AmbientEngine engine, int count)
    {
        for (int i = 0; i < count; i++)
            engine.OnPushFailed(new HttpRequestException("bridge unreachable"));
    }

    [Fact]
    public void APushIncidentIsAUserIncidentUntilAPushLands()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.HuePush);
        var engine = new AmbientEngine(new Host());

        FailPushes(engine, 2);
        Assert.False(IsOpen);

        FailPushes(engine, 1);
        UserIncident incident = Assert.Single(
            UserIncidentRegistry.Instance.Snapshot(), i => i.Id == UserIncidents.HuePush);
        Assert.Equal(nameof(HttpRequestException), incident.Reason);

        engine.OnPushSucceeded();
        Assert.False(IsOpen);
    }

    [Fact]
    public void TheEndOfTheRunClosesThePushUserIncident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.HuePush);
        var engine = new AmbientEngine(new Host());
        FailPushes(engine, 3);
        Assert.True(IsOpen);

        engine.AbandonOpenIncidents("w1");

        Assert.False(IsOpen);
    }
}
