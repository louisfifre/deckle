using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// A listener of the user incident registry is the App's tray code. When it
// throws, the module that opened the incident must not see it, the other
// listeners must still run, and the log must say once that the tray may be
// out of step, then say when it is back.
[Trait("Category", "observability")]
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class UserIncidentListenerFaultTests
{
    [Fact]
    public void AThrowingListenerNeitherReachesTheModuleNorStarvesTheOthers()
    {
        var registry = new UserIncidentRegistry();
        int healthyCalls = 0;
        registry.Changed += () => throw new InvalidOperationException("tray down");
        registry.Changed += () => healthyCalls++;

        registry.Open("mic", "probe");

        Assert.Equal(1, healthyCalls);
        Assert.Equal(1, registry.ListenerFailures);
        Assert.True(registry.IsOpen("mic"));
    }

    [Fact]
    public void TheFirstFailureIsReportedOnceAndTheNextCleanNotificationClosesIt()
    {
        using var listener = new TestEventListener("Deckle-Diagnostics");
        var registry = new UserIncidentRegistry();
        bool failing = true;
        string marker = "tray down " + Guid.NewGuid().ToString("N");
        registry.Changed += () => { if (failing) throw new InvalidOperationException(marker); };

        registry.Open("a", "x");
        registry.Open("b", "x");
        failing = false;
        registry.Close("a");

        EventWrittenEventArgs fault = Assert.Single(
            listener.Events,
            e => e.EventId == DeckleDiagnosticsSource.EvtIncidentListenerFaulted
                && Equals(e.Payload?[2], marker));
        Assert.Equal(EventLevel.Warning, fault.Level);
        Assert.Equal(nameof(InvalidOperationException), fault.Payload![1]);

        EventWrittenEventArgs recovered = Assert.Single(
            listener.Events,
            e => e.EventId == DeckleDiagnosticsSource.EvtIncidentListenerRecovered);
        Assert.Equal(2, recovered.Payload![0]);
        Assert.NotEqual(EventTags.None, recovered.Tags & ObservationTags.Milestone);
    }
}
