using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Deckle.Diagnostics;
using Deckle.Notifications;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Notifications.Tests;

// Observability: pins the narrative the notification subsystem emits on the
// Deckle-Notifications provider. Every event carries the transverse Push
// keyword, and each Info milestone is mirrored by its Verbose detail — the
// Info/Verbose separation from Deckle.Diagnostics/CLAUDE.md. A TestEventListener
// attached by ETW name collects the sequence; the test asserts on order,
// levels, and the id-bearing payloads.
// Same collection as NotificationDispatcherTests: both drive the process-wide
// NotificationDispatcher singleton, whose Initialize emits on the shared
// provider — running the classes in parallel makes each listener see the
// other class's emissions.
[Collection("notification dispatcher singleton")]
[Trait("Category", "observability")]
public class DeckleNotificationsSourceTests
{
    [Fact]
    public void InitializeEmitsDispatcherInitializedInfoThenItsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Notifications");
        var channel = new FakeNotificationChannel();

        NotificationDispatcher.Initialize(channel);

        var init = Single(listener, DeckleNotificationsSource.EvtDispatcherInitialized);
        Assert.Equal(EventLevel.Informational, init.Level);
        Assert.True(init.HasKeyword(Keywords.Push));

        var detail = Single(listener, DeckleNotificationsSource.EvtDispatcherInitializedDetail);
        Assert.Equal(EventLevel.Verbose, detail.Level);
        // detail carries the channel names and their count.
        Assert.Equal("Toast", detail.Payload?[0]);
        Assert.Equal(1, detail.Payload?[1]);

        // The Verbose detail follows the Info milestone, never precedes it.
        AssertOrdered(listener,
            DeckleNotificationsSource.EvtDispatcherInitialized,
            DeckleNotificationsSource.EvtDispatcherInitializedDetail);
    }

    [Fact]
    public async Task SuccessfulPromptEmitsShownThenRespondedEachWithItsVerboseMirror()
    {
        var dispatcher = NotificationDispatcher.Initialize(
            new FakeNotificationChannel(cannedResponse: new NotificationResponse("ok", null)));
        var descriptor = Descriptors.Make("playground.shown", severity: NotificationSeverity.Warning);
        dispatcher.Catalog.Register(new[] { descriptor });

        // Attach only around the prompt so init events from above stay out.
        using var listener = new TestEventListener("Deckle-Notifications");
        await dispatcher.PromptAsync(descriptor, ct: TestContext.Current.CancellationToken);

        var shown = Single(listener, DeckleNotificationsSource.EvtNotificationShown);
        Assert.Equal(EventLevel.Informational, shown.Level);
        Assert.True(shown.HasKeyword(Keywords.Push));

        var shownDetail = Single(listener, DeckleNotificationsSource.EvtNotificationShownDetail);
        Assert.Equal(EventLevel.Verbose, shownDetail.Level);
        Assert.Equal("playground.shown", shownDetail.Payload?[0]); // notification_id
        Assert.Equal("Toast", shownDetail.Payload?[1]);            // channel
        Assert.Equal("Warning", shownDetail.Payload?[2]);          // severity

        var responded = Single(listener, DeckleNotificationsSource.EvtNotificationResponded);
        Assert.Equal(EventLevel.Informational, responded.Level);

        var respondedDetail = Single(listener, DeckleNotificationsSource.EvtNotificationRespondedDetail);
        Assert.Equal(EventLevel.Verbose, respondedDetail.Level);
        Assert.Equal("playground.shown", respondedDetail.Payload?[0]); // notification_id
        Assert.Equal("ok", respondedDetail.Payload?[1]);               // action_id

        AssertOrdered(listener,
            DeckleNotificationsSource.EvtNotificationShown,
            DeckleNotificationsSource.EvtNotificationShownDetail,
            DeckleNotificationsSource.EvtNotificationResponded,
            DeckleNotificationsSource.EvtNotificationRespondedDetail);
    }

    [Fact]
    public async Task ADroppedPromptEmitsWarningWithTheReasonDetail()
    {
        // Channel unavailable -> the prompt drops; reason is "channel_unavailable".
        var dispatcher = NotificationDispatcher.Initialize(
            new FakeNotificationChannel(isAvailable: false));
        var descriptor = Descriptors.Make("playground.dropped");
        dispatcher.Catalog.Register(new[] { descriptor });

        using var listener = new TestEventListener("Deckle-Notifications");
        await dispatcher.PromptAsync(descriptor, ct: TestContext.Current.CancellationToken);

        var dropped = Single(listener, DeckleNotificationsSource.EvtNotificationDropped);
        Assert.Equal(EventLevel.Warning, dropped.Level);
        Assert.True(dropped.HasKeyword(Keywords.Push));

        var droppedDetail = Single(listener, DeckleNotificationsSource.EvtNotificationDroppedDetail);
        Assert.Equal(EventLevel.Verbose, droppedDetail.Level);
        Assert.Equal("playground.dropped", droppedDetail.Payload?[0]); // notification_id
        Assert.Equal("channel_unavailable", droppedDetail.Payload?[1]); // reason

        // No show / responded events on a drop.
        Assert.DoesNotContain(listener.Events,
            e => e.EventId == DeckleNotificationsSource.EvtNotificationShown);
        Assert.DoesNotContain(listener.Events,
            e => e.EventId == DeckleNotificationsSource.EvtNotificationResponded);
    }

    [Fact]
    public async Task ADropWithNoChannelCarriesTheNoChannelReason()
    {
        var dispatcher = NotificationDispatcher.Initialize();
        var descriptor = Descriptors.Make("playground.no_channel_reason");
        dispatcher.Catalog.Register(new[] { descriptor });

        using var listener = new TestEventListener("Deckle-Notifications");
        await dispatcher.PromptAsync(descriptor, ct: TestContext.Current.CancellationToken);

        var droppedDetail = Single(listener, DeckleNotificationsSource.EvtNotificationDroppedDetail);
        Assert.Equal("no_channel", droppedDetail.Payload?[1]); // reason
    }

    [Fact]
    public async Task CancellingAShownPromptClosesTheStepWithCancelledThenItsVerboseMirror()
    {
        // Park the channel so the token has a window to fire while the prompt is
        // shown-but-unanswered — the exact moment the cancellation narrative is
        // meant to cover.
        var channel = new FakeNotificationChannel
        {
            PendingCompletion = new TaskCompletionSource<NotificationResponse?>(),
        };
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.cancel_obs");
        dispatcher.Catalog.Register(new[] { descriptor });

        using var listener = new TestEventListener("Deckle-Notifications");
        using var cts = new CancellationTokenSource();
        var prompt = dispatcher.PromptAsync(descriptor, ct: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => prompt);

        // The closing Info: cancellation is caller-initiated and benign.
        var cancelled = Single(listener, DeckleNotificationsSource.EvtNotificationCancelled);
        Assert.Equal(EventLevel.Informational, cancelled.Level);
        Assert.True(cancelled.HasKeyword(Keywords.Push));

        // Its Verbose mirror carries the id.
        var promptCancelled = Single(listener, DeckleNotificationsSource.EvtPromptCancelled);
        Assert.Equal(EventLevel.Verbose, promptCancelled.Level);
        Assert.True(promptCancelled.HasKeyword(Keywords.Push));
        Assert.Equal("playground.cancel_obs", promptCancelled.Payload?[0]); // notification_id

        // The step opened at Shown and closes at Cancelled, the mirror last.
        AssertOrdered(listener,
            DeckleNotificationsSource.EvtNotificationShown,
            DeckleNotificationsSource.EvtNotificationCancelled,
            DeckleNotificationsSource.EvtPromptCancelled);
    }

    [Fact]
    public async Task AnUnansweredPromptClosesTheStepWithUnansweredAndNoResponded()
    {
        // The channel was shown but settled with null — shown-but-never-answered,
        // distinct from a drop (which never shows).
        var dispatcher = NotificationDispatcher.Initialize(
            new FakeNotificationChannel(returnsNull: true));
        var descriptor = Descriptors.Make("playground.unanswered_obs");
        dispatcher.Catalog.Register(new[] { descriptor });

        using var listener = new TestEventListener("Deckle-Notifications");
        var response = await dispatcher.PromptAsync(
            descriptor, ct: TestContext.Current.CancellationToken);

        Assert.Null(response);

        var unanswered = Single(listener, DeckleNotificationsSource.EvtNotificationUnanswered);
        Assert.Equal(EventLevel.Informational, unanswered.Level);
        Assert.True(unanswered.HasKeyword(Keywords.Push));

        var unansweredDetail = Single(listener, DeckleNotificationsSource.EvtNotificationUnansweredDetail);
        Assert.Equal(EventLevel.Verbose, unansweredDetail.Level);
        Assert.True(unansweredDetail.HasKeyword(Keywords.Push));
        Assert.Equal("playground.unanswered_obs", unansweredDetail.Payload?[0]); // notification_id
        Assert.Equal("Toast", unansweredDetail.Payload?[1]);                     // channel

        // Shown still opens the step; the unanswered close follows it.
        AssertOrdered(listener,
            DeckleNotificationsSource.EvtNotificationShown,
            DeckleNotificationsSource.EvtNotificationUnanswered,
            DeckleNotificationsSource.EvtNotificationUnansweredDetail);

        // A no-answer outcome is never a response.
        Assert.DoesNotContain(listener.Events,
            e => e.EventId == DeckleNotificationsSource.EvtNotificationResponded);
        Assert.DoesNotContain(listener.Events,
            e => e.EventId == DeckleNotificationsSource.EvtNotificationRespondedDetail);
    }

    [Fact]
    public async Task AChannelThatThrowsClosesTheStepWithFailedAndRethrows()
    {
        // The channel fails while delivering. The dispatcher closes the open
        // step at Warning and lets the exception escape.
        var channel = new FakeNotificationChannel
        {
            ThrowOnPrompt = new InvalidOperationException("boom"),
        };
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.failed_obs");
        dispatcher.Catalog.Register(new[] { descriptor });

        using var listener = new TestEventListener("Deckle-Notifications");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.PromptAsync(descriptor, ct: TestContext.Current.CancellationToken));

        var failed = Single(listener, DeckleNotificationsSource.EvtNotificationFailed);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.True(failed.HasKeyword(Keywords.Push));

        var failedDetail = Single(listener, DeckleNotificationsSource.EvtNotificationFailedDetail);
        Assert.Equal(EventLevel.Verbose, failedDetail.Level);
        Assert.True(failedDetail.HasKeyword(Keywords.Push));
        Assert.Equal("playground.failed_obs", failedDetail.Payload?[0]); // notification_id
        Assert.Equal("boom", failedDetail.Payload?[1]);                  // error

        AssertOrdered(listener,
            DeckleNotificationsSource.EvtNotificationShown,
            DeckleNotificationsSource.EvtNotificationFailed,
            DeckleNotificationsSource.EvtNotificationFailedDetail);

        // A failure is never a response.
        Assert.DoesNotContain(listener.Events,
            e => e.EventId == DeckleNotificationsSource.EvtNotificationResponded);
    }

    [Fact]
    public void RegisteringDescriptorsEmitsTheCatalogAuditWithItsIdsAndCount()
    {
        var dispatcher = NotificationDispatcher.Initialize(new FakeNotificationChannel());

        // Attach BEFORE Register so the catalogue audit lands on this listener —
        // the other suites deliberately attach after, to keep init/registration
        // noise out of their assertions.
        using var listener = new TestEventListener("Deckle-Notifications");
        dispatcher.Catalog.Register(new[]
        {
            Descriptors.Make("playground.cat_a"),
            Descriptors.Make("playground.cat_b"),
        });

        var registered = Single(listener, DeckleNotificationsSource.EvtCatalogRegistered);
        Assert.Equal(EventLevel.Verbose, registered.Level);
        Assert.True(registered.HasKeyword(Keywords.Push));
        Assert.Equal("playground.cat_a,playground.cat_b", registered.Payload?[0]); // notification_ids
        Assert.Equal(2, registered.Payload?[1]);                                   // descriptor_count
    }

    // ── helpers ────────────────────────────────────────────────────────────

    // The provider is a process-wide singleton; concurrent tests in other
    // suites could in principle emit onto the same listener. Selecting by
    // EventId keeps each assertion robust to that without serializing the run.
    private static EventWrittenEventArgs Single(TestEventListener listener, int eventId)
        => Assert.Single(listener.Events, e => e.EventId == eventId);

    private static void AssertOrdered(TestEventListener listener, params int[] eventIds)
    {
        var positions = eventIds
            .Select(id => listener.Events.ToList().FindIndex(e => e.EventId == id))
            .ToArray();
        for (int i = 1; i < positions.Length; i++)
        {
            Assert.True(positions[i - 1] >= 0 && positions[i] > positions[i - 1],
                $"Event {eventIds[i]} must follow event {eventIds[i - 1]}.");
        }
    }
}
