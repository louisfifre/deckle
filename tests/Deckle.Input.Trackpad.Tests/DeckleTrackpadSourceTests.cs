using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

[Trait("Category", "observability")]
public sealed class DeckleTrackpadSourceTests : IDisposable
{
    public DeckleTrackpadSourceTests()
    {
        OperationalLogAdmission.Configure(static _ => false);
    }

    public void Dispose()
    {
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void InjectionIncidentAndRecoveryRemainPermanent()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.InjectionFailed();
        DeckleTrackpadSource.Log.InjectionRecovered();

        Assert.Collection(
            listener.Events,
            incident =>
            {
                Assert.Equal(DeckleTrackpadSource.EvtInjectionFailed, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
            },
            recovery =>
            {
                Assert.Equal(DeckleTrackpadSource.EvtInjectionRecovered, recovery.EventId);
                Assert.Equal(EventLevel.Informational, recovery.Level);
            });
    }

    [Fact]
    public void GestureDetailsFollowInputActivityWhileLifecycleRemainsAdmitted()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.DragStarted();
        DeckleTrackpadSource.Log.DragEnded("lift", 120, 8);
        DeckleTrackpadSource.Log.TapIgnored();
        DeckleTrackpadSource.Log.EngineStarted();

        Assert.Single(listener.Events);
        Assert.Equal(DeckleTrackpadSource.EvtEngineStarted, listener.Events[0].EventId);

        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Input);
        DeckleTrackpadSource.Log.DragStarted();
        DeckleTrackpadSource.Log.DragEnded("lift", 120, 8);
        DeckleTrackpadSource.Log.TapIgnored();

        Assert.Collection(
            listener.Events.Skip(1),
            started => Assert.Equal(DeckleTrackpadSource.EvtDragStarted, started.EventId),
            ended => Assert.Equal(DeckleTrackpadSource.EvtDragEnded, ended.EventId),
            tap => Assert.Equal(DeckleTrackpadSource.EvtTapIgnored, tap.EventId));
    }
}
