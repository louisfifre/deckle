using System;
using System.Diagnostics.Tracing;
using System.Linq;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Notifications.Tests;

// A toast that cannot be withdrawn used to vanish without a trace. These tests
// pin the log's view of it: the first failure is one Warning with its cause,
// the failures that follow add nothing, and the first success after them is
// one recovery. Same collection as the other classes that count events on the
// shared Deckle-Notifications provider.
[Collection("notification dispatcher singleton")]
[Trait("Category", "regression")]
public class ToastWithdrawIncidentTests
{
    [Fact]
    public void A_failed_withdrawal_is_one_warning_that_names_the_cause()
    {
        using var listener = new TestEventListener("Deckle-Notifications");
        var incident = new ToastWithdrawIncident();

        incident.Failed(new InvalidOperationException("service stopped"));

        EventWrittenEventArgs ev = Assert.Single(
            listener.Events, e => e.EventId == DeckleNotificationsSource.EvtToastWithdrawFailed);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal(["ex_type", "ex_message"], ev.PayloadNames);
        Assert.Equal("InvalidOperationException", ev.Payload?[0]);
        Assert.Equal("service stopped", ev.Payload?[1]);
    }

    [Fact]
    public void Withdrawals_that_keep_failing_stay_one_incident_closed_by_one_recovery()
    {
        using var listener = new TestEventListener("Deckle-Notifications");
        var incident = new ToastWithdrawIncident();

        incident.Failed(new InvalidOperationException("first"));
        incident.Failed(new InvalidOperationException("second"));
        incident.Failed(new InvalidOperationException("third"));
        incident.Succeeded();
        incident.Succeeded();

        Assert.Single(listener.Events, e => e.EventId == DeckleNotificationsSource.EvtToastWithdrawFailed);
        EventWrittenEventArgs recovered = Assert.Single(
            listener.Events, e => e.EventId == DeckleNotificationsSource.EvtToastWithdrawRecovered);
        Assert.Equal(EventLevel.Informational, recovered.Level);
        Assert.Equal(ObservationTags.Milestone, recovered.Tags & ObservationTags.Milestone);
    }

    [Fact]
    public void A_withdrawal_that_never_failed_writes_nothing()
    {
        using var listener = new TestEventListener("Deckle-Notifications");
        var incident = new ToastWithdrawIncident();

        incident.Succeeded();

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId is DeckleNotificationsSource.EvtToastWithdrawFailed
                      or DeckleNotificationsSource.EvtToastWithdrawRecovered);
    }

    [Fact]
    public void A_failure_after_a_recovery_opens_a_new_incident()
    {
        using var listener = new TestEventListener("Deckle-Notifications");
        var incident = new ToastWithdrawIncident();

        incident.Failed(new InvalidOperationException("one"));
        incident.Succeeded();
        incident.Failed(new InvalidOperationException("two"));

        Assert.Equal(2, listener.Events.Count(
            e => e.EventId == DeckleNotificationsSource.EvtToastWithdrawFailed));
    }
}
