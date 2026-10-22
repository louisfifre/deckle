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
    public void InjectionIncidentAndItsEndRemainPermanent()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.InjectionFailed("move", 5);
        DeckleTrackpadSource.Log.InjectionIncidentEnded("injection_succeeded");

        Assert.Collection(
            listener.Events,
            incident =>
            {
                Assert.Equal(DeckleTrackpadSource.EvtInjectionFailed, incident.EventId);
                Assert.Equal(EventLevel.Warning, incident.Level);
                Assert.Equal("move", incident.Payload?[0]);
                Assert.Equal(5, incident.Payload?[1]);
            },
            ended =>
            {
                Assert.Equal(DeckleTrackpadSource.EvtInjectionIncidentEnded, ended.EventId);
                Assert.Equal(EventLevel.Informational, ended.Level);
                Assert.Equal(ObservationTags.Journal, ended.Tags & ObservationTags.Journal);
                Assert.Equal("injection_succeeded", ended.Payload?[0]);
            });
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ASystemSettingChangeAndTheRepairScriptReachTheJournalOnSuccessToo()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.GesturesNeutralized();
        DeckleTrackpadSource.Log.GesturesRestored();
        DeckleTrackpadSource.Log.RepairLaunched();
        DeckleTrackpadSource.Log.RepairCompleted(0);

        Assert.Equal(4, listener.Events.Count);
        Assert.All(listener.Events, e => Assert.Equal(ObservationTags.Journal, e.Tags & ObservationTags.Journal));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFailedButtonReleaseIsAWarningWithItsWin32Error()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.PrimaryReleaseFailed(5);

        var warning = Assert.Single(listener.Events);
        Assert.Equal(DeckleTrackpadSource.EvtPrimaryReleaseFailed, warning.EventId);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Equal(5, warning.Payload?[0]);
    }

    [Fact]
    public void GestureDetailsFollowInputActivityWhileLifecycleRemainsAdmitted()
    {
        using var listener = new TestEventListener("Deckle-Trackpad");

        DeckleTrackpadSource.Log.DragStarted();
        DeckleTrackpadSource.Log.DragEnded("lift", 120, 8);
        DeckleTrackpadSource.Log.TapIgnored();
        DeckleTrackpadSource.Log.EngineStarted(1.0, true);

        var started = Assert.Single(listener.Events);
        Assert.Equal(DeckleTrackpadSource.EvtEngineStarted, started.EventId);
        Assert.True(started.HasKeyword(Keywords.Lifecycle));
        Assert.Equal(ObservationTags.Journal, started.Tags & ObservationTags.Journal);

        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Input);
        DeckleTrackpadSource.Log.DragStarted();
        DeckleTrackpadSource.Log.DragEnded("lift", 120, 8);
        DeckleTrackpadSource.Log.TapIgnored();

        Assert.Collection(
            listener.Events.Skip(1),
            dragStarted => Assert.Equal(DeckleTrackpadSource.EvtDragStarted, dragStarted.EventId),
            ended => Assert.Equal(DeckleTrackpadSource.EvtDragEnded, ended.EventId),
            tap => Assert.Equal(DeckleTrackpadSource.EvtTapIgnored, tap.EventId));
    }
}
