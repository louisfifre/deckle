using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueEventStreamEpisodeTests
{
    [Fact]
    public void IncidentOpensOnceAfterGracePeriod()
    {
        var time = new TestTimeProvider();
        var episode = new HueEventStreamEpisode(time);
        HueEventStreamLoss loss = episode.RecordLoss(new HttpRequestException("offline"));

        time.Advance(HueEventStreamEpisode.IncidentDelay - TimeSpan.FromMilliseconds(1));
        Assert.False(episode.TryOpenIncident(loss.Generation, out _));

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(episode.TryOpenIncident(loss.Generation, out HueEventStreamObservation incident));
        Assert.Equal(1, incident.FailureCount);
        Assert.False(episode.TryOpenIncident(loss.Generation, out _));
    }

    [Fact]
    public void BriefLossDoesNotProduceRecovery()
    {
        var time = new TestTimeProvider();
        var episode = new HueEventStreamEpisode(time);
        episode.RecordLoss(new HttpRequestException("offline"));

        time.Advance(HueEventStreamEpisode.IncidentDelay - TimeSpan.FromMilliseconds(1));

        Assert.False(episode.TryRecover(out _));
    }

    [Fact]
    public void ReconnectionClosesOpenIncidentOnce()
    {
        var time = new TestTimeProvider();
        var episode = new HueEventStreamEpisode(time);
        HueEventStreamLoss loss = episode.RecordLoss(new HttpRequestException("offline"));
        episode.RecordLoss(new HttpRequestException("still offline"));
        time.Advance(HueEventStreamEpisode.IncidentDelay);
        Assert.True(episode.TryOpenIncident(loss.Generation, out _));

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(episode.TryRecover(out HueEventStreamObservation recovery));
        Assert.Equal(2, recovery.FailureCount);
        Assert.Equal(TimeSpan.FromSeconds(7), recovery.Duration);
        Assert.False(episode.TryRecover(out _));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
