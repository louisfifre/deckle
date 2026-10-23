using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class RepeatingIncidentTests
{
    [Fact]
    public void OpensOnceWhenTheStreakReachesTheThreshold()
    {
        const int threshold = 3;
        const int burst = 5;
        var incident = new RepeatingIncident(threshold, quietMs: 0);

        Assert.False(incident.Record(0));
        Assert.False(incident.Record(1));
        Assert.True(incident.Record(2));
        Assert.True(incident.IsOpen);

        // Further occurrences join the open incident instead of opening another,
        // and the incident counts the whole streak: the occurrences that opened
        // it, the single one after, and the burst.
        Assert.False(incident.Record(3));
        Assert.False(incident.Record(4, occurrences: burst));
        Assert.Equal(threshold + 1 + burst, incident.Occurrences);
    }

    [Fact]
    public void ClearBeforeTheThresholdForgetsIsolatedOccurrences()
    {
        var incident = new RepeatingIncident(threshold: 3, quietMs: 0);

        incident.Record(0);
        incident.Record(1);
        Assert.False(incident.Clear(2, out int total));
        Assert.Equal(0, total);

        // The streak restarted, so two more occurrences do not reach three.
        Assert.False(incident.Record(3));
        Assert.False(incident.Record(4));
        Assert.False(incident.IsOpen);
    }

    [Fact]
    public void ClearWaitsForTheQuietPeriodThenReportsTheTotal()
    {
        var incident = new RepeatingIncident(threshold: 1, quietMs: 100);

        Assert.True(incident.Record(0));
        incident.Record(50);

        Assert.False(incident.Clear(120, out _));
        Assert.True(incident.IsOpen);

        Assert.True(incident.Clear(150, out int total));
        Assert.Equal(2, total);
        Assert.False(incident.IsOpen);
        Assert.Equal(0, incident.Occurrences);
    }

    [Fact]
    public void CloseEndsAnOpenIncidentWhateverTheQuietPeriod()
    {
        var incident = new RepeatingIncident(threshold: 1, quietMs: 30_000);
        incident.Record(0, occurrences: 4);

        Assert.True(incident.Close(out int total));
        Assert.Equal(4, total);
        Assert.False(incident.IsOpen);

        Assert.False(incident.Close(out int again));
        Assert.Equal(0, again);
    }

    [Fact]
    public void ZeroOccurrencesRecordNothing()
    {
        var incident = new RepeatingIncident(threshold: 1, quietMs: 0);

        Assert.False(incident.Record(0, occurrences: 0));
        Assert.False(incident.IsOpen);
        Assert.Equal(0, incident.Occurrences);
    }
}
