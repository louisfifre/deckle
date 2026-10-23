using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Lighting.Tests;

// The tray shows a bridge whose event stream stays lost past the grace period,
// until the stream reconnects or its subscriber stops. The collection runs
// alone, so the process-wide registry sees only these tests.
[Trait("Category", "observability")]
[Collection(LightingObservabilityCollection.Name)]
public sealed class HueEventStreamUserIncidentTests
{
    private static bool IsOpen => UserIncidentRegistry.Instance.IsOpen(UserIncidents.HueEvents);

    [Fact]
    public void ALossPastTheGracePeriodIsAUserIncidentUntilTheStreamReconnects()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.HueEvents);
        var time = new SteppedTime();
        var episode = new HueEventStreamEpisode(time);
        HueEventStreamLoss loss = episode.RecordLoss(new HttpRequestException("offline"));

        time.Advance(HueEventStreamEpisode.IncidentDelay - TimeSpan.FromMilliseconds(1));
        episode.TryOpenIncident(loss.Generation, out _);
        Assert.False(IsOpen);

        time.Advance(TimeSpan.FromMilliseconds(1));
        episode.TryOpenIncident(loss.Generation, out _);
        UserIncident incident = Assert.Single(
            UserIncidentRegistry.Instance.Snapshot(), i => i.Id == UserIncidents.HueEvents);
        Assert.Equal(nameof(HttpRequestException), incident.Reason);

        episode.TryRecover(out _);
        Assert.False(IsOpen);
    }

    [Fact]
    public void StoppingTheSubscriberClosesTheUserIncident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.HueEvents);
        var time = new SteppedTime();
        var episode = new HueEventStreamEpisode(time);
        HueEventStreamLoss loss = episode.RecordLoss(exception: null);
        time.Advance(HueEventStreamEpisode.IncidentDelay);
        episode.TryOpenIncident(loss.Generation, out _);
        Assert.True(IsOpen);

        episode.TryAbandon(out _);

        Assert.False(IsOpen);
    }

    private sealed class SteppedTime : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
