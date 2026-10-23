using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Orchestration.Tests;

// What the log says when a subscriber or an exit handler keeps throwing, and
// that the session and rule milestones name the entry they are about. The
// incident tests pick their events by the pid they raise, since other test
// classes of this project emit on the same provider in parallel.
[Trait("Category", "observability")]
public sealed class OrchestrationIncidentTests
{
    private const string Provider = "Deckle-Orchestration";

    // A pid no running process has; OpenProcess answers ERROR_INVALID_PARAMETER,
    // so ProcessExitWatch reports the exit at once, inline.
    private const int GonePid = 0x7FFFF0F0;

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static bool TravelsInTheLog(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Milestone) != 0;

    [Fact]
    [Trait("Category", "regression")]
    public void AForegroundSubscriberThatKeepsFailingIsOneWarningThenOneRecovery()
    {
        using var listener = new TestEventListener(Provider);
        var incident = new FailureIncident();
        var boom = new InvalidOperationException("boom");

        WatcherForegroundMonitor.LogDeliveryFailed(incident, pid: 7101, boom);
        WatcherForegroundMonitor.LogDeliveryFailed(incident, pid: 7102, boom);
        WatcherForegroundMonitor.LogDeliveryFailed(incident, pid: 7103, boom);

        EventWrittenEventArgs opener = Assert.Single(
            listener.Events, e => e.EventId == DeckleOrchestrationSource.EvtForegroundDeliveryFailed);
        Assert.Equal(EventLevel.Warning, opener.Level);
        Assert.Equal("7101", Field(opener, "pid"));
        Assert.Equal(nameof(InvalidOperationException), Field(opener, "ex_type"));

        EventWrittenEventArgs[] details = listener.Events
            .Where(e => e.EventId == DeckleOrchestrationSource.EvtForegroundDeliveryFailedDetail).ToArray();
        Assert.Equal(2, details.Length);
        Assert.All(details, e => Assert.Equal(EventLevel.Verbose, e.Level));
        Assert.All(details, e => Assert.False(TravelsInTheLog(e)));

        // A later failure after a recovery opens a new incident.
        Assert.True(incident.Close());
        Assert.False(incident.Close());
        WatcherForegroundMonitor.LogDeliveryFailed(incident, pid: 7104, boom);
        Assert.Equal(
            2,
            listener.Events.Count(e => e.EventId == DeckleOrchestrationSource.EvtForegroundDeliveryFailed));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AnExitHandlerThatKeepsFailingIsOneWarningAndItsSuccessIsTheRecovery()
    {
        using var listener = new TestEventListener(Provider);
        using var watch = new ProcessExitWatch();

        Assert.True(watch.Watch(GonePid, _ => throw new InvalidOperationException("first")));
        Assert.True(watch.Watch(GonePid, _ => throw new InvalidOperationException("second")));

        EventWrittenEventArgs opener = Assert.Single(
            listener.Events,
            e => e.EventId == DeckleOrchestrationSource.EvtExitWatchCallbackFailed);
        Assert.Equal(EventLevel.Warning, opener.Level);
        Assert.Equal(GonePid.ToString(), Field(opener, "pid"));
        Assert.Single(
            listener.Events,
            e => e.EventId == DeckleOrchestrationSource.EvtExitWatchCallbackFailedDetail);

        Assert.True(watch.Watch(GonePid, _ => { }));

        EventWrittenEventArgs recovered = Assert.Single(
            listener.Events,
            e => e.EventId == DeckleOrchestrationSource.EvtExitWatchCallbackRecovered);
        Assert.True(TravelsInTheLog(recovered));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void EveryPayloadOfTheProviderAppearsInItsMessageTemplate()
    {
        // A payload missing from the template is dropped from the line the log
        // keeps, which is how the session handler's entry id went unread. An
        // Informational template is a sentence alone by doctrine: its values
        // travel in the payload of the JSONL line, so it is not checked here.
        foreach (var declared in ProviderManifest.EventsOf(typeof(DeckleOrchestrationSource)))
        {
            System.Reflection.MethodInfo method = typeof(DeckleOrchestrationSource).GetMethod(declared.Name)!;
            EventAttribute attribute = method
                .GetCustomAttributes(typeof(EventAttribute), inherit: false)
                .Cast<EventAttribute>().Single();
            if (attribute.Level == EventLevel.Informational) continue;
            string template = attribute.Message ?? "";
            int count = method.GetParameters().Length;
            for (int i = 0; i < count; i++)
            {
                Assert.True(
                    template.Contains("{" + i + "}", StringComparison.Ordinal),
                    $"{declared.Name}: payload {method.GetParameters()[i].Name} is missing from its message template.");
            }
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ASessionAndItsRulesPairByTheEntryTheyNameInTheLog()
    {
        using var listener = new TestEventListener(Provider);
        var entry = new AppEntry { DisplayName = "Hollow", ProcessName = "Hollow" };
        var effect = new ProfileTransactionTests.FakeEffect("hdr", []);

        OrchestrationRuntime.LogSessionStarted(entry, pid: 4343);
        ProfileTransaction.Apply(entry, [effect]).Restore();
        OrchestrationRuntime.LogSessionEnded(entry, SessionEndReason.EntryDisabled);

        string[] story = listener.Events
            .Where(TravelsInTheLog)
            .Where(e => Equals(e.Payload?[0], entry.Id))
            .Select(e => e.EventName!)
            .ToArray();
        Assert.Equal(
            [
                nameof(DeckleOrchestrationSource.SessionStarted),
                nameof(DeckleOrchestrationSource.ProfileApplied),
                nameof(DeckleOrchestrationSource.ProfileRestored),
                nameof(DeckleOrchestrationSource.SessionEnded),
            ],
            story);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFailedSessionHandlerAndARuleNameTheEntryInTheirLine()
    {
        string[] names = ["SessionHandlerFailed", "RuleApplyFailed", "RuleRestoreFailed"];
        foreach (string name in names)
        {
            string template = typeof(DeckleOrchestrationSource).GetMethod(name)!
                .GetCustomAttributes(typeof(EventAttribute), inherit: false)
                .Cast<EventAttribute>().Single().Message!;
            Assert.Contains("entry_id=", template);
        }
    }
}
