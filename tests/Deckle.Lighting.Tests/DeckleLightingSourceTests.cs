using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "observability")]
[Collection(LightingObservabilityCollection.Name)]
public sealed class DeckleLightingSourceTests : IDisposable
{
    public void Dispose()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Ambient, false);
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void EventStreamIncidentAndRecoveryRemainWhenAmbientDetailIsDisabled()
    {
        OperationalLogAdmission.Configure(static _ => false);
        OperationalLogAdmission.SetActive(OperationalLogActivity.Ambient, true);
        using var listener = new TestEventListener("Deckle-Lighting");

        DeckleLightingSource.Log.EventStreamIncident();
        DeckleLightingSource.Log.EventStreamIncidentDetail(5000, 3, "exception", "IOException", "offline");
        DeckleLightingSource.Log.EventStreamRecovered();
        DeckleLightingSource.Log.EventStreamRecoveryDetail(7000, 4);

        Assert.Collection(
            listener.Events,
            incident =>
            {
                Assert.Equal(DeckleLightingSource.EvtEventStreamIncident, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
            },
            recovery =>
            {
                Assert.Equal(DeckleLightingSource.EvtEventStreamRecovered, recovery.EventId);
                Assert.Equal(EventLevel.Informational, recovery.Level);
            });
    }

    [Fact]
    public void EventStreamEpisodeDetailsAreAdmittedOnceEnabled()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Ambient);
        OperationalLogAdmission.SetActive(OperationalLogActivity.Ambient, true);
        using var listener = new TestEventListener("Deckle-Lighting");

        DeckleLightingSource.Log.EventStreamIncidentDetail(5000, 3, "exception", "IOException", "offline");
        DeckleLightingSource.Log.EventStreamRecoveryDetail(7000, 4);

        Assert.Equal(
            [
                DeckleLightingSource.EvtEventStreamIncidentDetail,
                DeckleLightingSource.EvtEventStreamRecoveryDetail,
            ],
            listener.Events.Select(static observation => observation.EventId));
    }
}
