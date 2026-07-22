using System.Diagnostics.Tracing;
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

        DecklePrecisionScrollSource.Log.EngineStarted();
        DecklePrecisionScrollSource.Log.InjectionFailed();
        DecklePrecisionScrollSource.Log.InjectionFailedDetail("move", 5);
        DecklePrecisionScrollSource.Log.EngineStopped();
        DecklePrecisionScrollSource.Log.EngineStoppedDetail(12, 2, 0);

        Assert.Collection(
            listener.Events,
            started =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtEngineStarted, started.EventId);
                Assert.Equal(EventLevel.Informational, started.Level);
            },
            incident =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtInjectionFailed, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
            },
            detail => Assert.Equal(
                DecklePrecisionScrollSource.EvtInjectionFailedDetail,
                detail.EventId),
            stopped => Assert.Equal(
                DecklePrecisionScrollSource.EvtEngineStopped,
                stopped.EventId),
            summary => Assert.Equal(
                DecklePrecisionScrollSource.EvtEngineStoppedDetail,
                summary.EventId));
    }
}
