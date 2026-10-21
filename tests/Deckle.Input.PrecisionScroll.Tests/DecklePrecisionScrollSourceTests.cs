using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "observability")]
public sealed class DecklePrecisionScrollSourceTests
{
    [Fact]
    public void LifecycleAndInjectionIncidentRemainDistinct()
    {
        using var listener = new TestEventListener("Deckle-PrecisionScroll");

        DecklePrecisionScrollSource.Log.EngineStarted(false, true);
        DecklePrecisionScrollSource.Log.InjectionFailed("move", 5);
        DecklePrecisionScrollSource.Log.EngineStopped("failed", "injection_failed");
        DecklePrecisionScrollSource.Log.EngineStoppedDetail(12, 2, 0);

        Assert.Collection(
            listener.Events,
            started =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtEngineStarted, started.EventId);
                Assert.Equal(EventLevel.Informational, started.Level);
                Assert.Equal(ObservationTags.Journal, started.Tags & ObservationTags.Journal);
            },
            incident =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtInjectionFailed, incident.EventId);
                Assert.Equal(EventLevel.Error, incident.Level);
                Assert.Equal("move", incident.Payload?[0]);
                Assert.Equal(5, incident.Payload?[1]);
            },
            stopped =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtEngineStopped, stopped.EventId);
                Assert.Equal(ObservationTags.Journal, stopped.Tags & ObservationTags.Journal);
                Assert.Equal("failed", stopped.Payload?[0]);
                Assert.Equal("injection_failed", stopped.Payload?[1]);
            },
            summary => Assert.Equal(
                DecklePrecisionScrollSource.EvtEngineStoppedDetail,
                summary.EventId));
    }

    [Fact]
    public void WorkerFaultIsAnErrorCarryingItsCause()
    {
        using var listener = new TestEventListener("Deckle-PrecisionScroll");

        DecklePrecisionScrollSource.Log.WorkerFaulted("InvalidOperationException", "boom");

        var faulted = Assert.Single(listener.Events);
        Assert.Equal(DecklePrecisionScrollSource.EvtWorkerFaulted, faulted.EventId);
        Assert.Equal(EventLevel.Error, faulted.Level);
        Assert.Equal("InvalidOperationException", faulted.Payload?[0]);
        Assert.Equal("boom", faulted.Payload?[1]);
    }
}
