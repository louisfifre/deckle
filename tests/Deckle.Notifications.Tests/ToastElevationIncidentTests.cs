using System.Linq;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Notifications.Tests;

// An elevated Deckle loses every toast, which only a relaunch without
// administrator rights fixes, so the tray shows it beside the log's Warning.
// The process cannot be elevated in a test: the seam is what the constructor
// calls once it found the process elevated. Same collection as the classes
// that listen to Deckle-Notifications, so they do not hear this Warning.
[Collection("notification dispatcher singleton")]
[Trait("Category", "observability")]
public sealed class ToastElevationIncidentTests
{
    [Fact]
    public void AnElevatedProcessIsAUserIncidentBesideTheWarning()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.ToastsElevated);
        using var listener = new TestEventListener("Deckle-Notifications");

        ToastChannel.ReportElevated();

        Assert.Single(listener.Events, e => e.EventId == DeckleNotificationsSource.EvtToastsUnavailable);
        UserIncident incident = Assert.Single(
            UserIncidentRegistry.Instance.Snapshot(), i => i.Id == UserIncidents.ToastsElevated);
        Assert.Equal("elevated", incident.Reason);
        UserIncidentRegistry.Instance.Close(UserIncidents.ToastsElevated);
    }
}
