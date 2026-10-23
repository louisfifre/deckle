using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "observability")]
[Collection(PrecisionScrollObservabilityCollection.Name)]
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
                Assert.Equal(ObservationTags.Milestone, started.Tags & ObservationTags.Milestone);
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
                Assert.Equal(ObservationTags.Milestone, stopped.Tags & ObservationTags.Milestone);
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

    // The settings store reports in free text. A damaged file at load is a
    // Warning (degraded, defaults in use) and a failed write an Error.
    [Fact]
    public void ADamagedSettingsFileSurfacesAsAWarningAndAFailedWriteAsAnError()
    {
        using var listener = new TestEventListener("Deckle-PrecisionScroll");

        DecklePrecisionScrollSource.Log.SettingsLoadWarning("reason=parse_failed");
        DecklePrecisionScrollSource.Log.SettingsLoadError("reason=write_failed");

        Assert.Collection(
            listener.Events,
            warning =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtSettingsLoadWarning, warning.EventId);
                Assert.Equal(EventLevel.Warning, warning.Level);
                Assert.Equal("reason=parse_failed", warning.Payload?[0]);
            },
            error =>
            {
                Assert.Equal(DecklePrecisionScrollSource.EvtSettingsLoadError, error.EventId);
                Assert.Equal(EventLevel.Error, error.Level);
                Assert.Equal("reason=write_failed", error.Payload?[0]);
            });
    }
}
