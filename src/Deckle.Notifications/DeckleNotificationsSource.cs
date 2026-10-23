using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Notifications;

// Observes the notification lifecycle (dispatcher boot, catalogue
// registration, and the show / responded / dropped / cancelled / unanswered /
// failed path of each prompt) plus the toast channel's two failure modes
// (elevated process, registration failure).
//
// Each Verbose mirror FOLLOWS its Info. Notifications are outputs sent to the
// world, hence the transverse `Push` keyword on every event.
[EventSource(Name = "Deckle-Notifications")]
public sealed class DeckleNotificationsSource : DeckleEventSource
{
    public static readonly DeckleNotificationsSource Log = new();

    private DeckleNotificationsSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; do not reuse an ID after deleting an
    // event.
    public const int EvtDispatcherInitialized       = 1;
    public const int EvtDispatcherInitializedDetail = 2;
    public const int EvtCatalogRegistered           = 3;
    public const int EvtNotificationShown           = 4;
    public const int EvtNotificationShownDetail     = 5;
    public const int EvtNotificationResponded       = 6;
    public const int EvtNotificationRespondedDetail = 7;
    public const int EvtNotificationDropped         = 8;
    // 9 retired: the Verbose mirror of NotificationDropped.
    public const int EvtPromptCancelled             = 10;
    public const int EvtToastsUnavailable           = 11;
    public const int EvtToastRegistrationFailed     = 12;
    // 13 retired: the Verbose mirror of ToastRegistrationFailed.
    public const int EvtNotificationCancelled       = 14;
    public const int EvtNotificationUnanswered      = 15;
    public const int EvtNotificationUnansweredDetail = 16;
    public const int EvtNotificationFailed          = 17;
    // 18 retired: the Verbose mirror of NotificationFailed.
    public const int EvtToastWithdrawFailed         = 19;
    public const int EvtToastWithdrawRecovered      = 20;

    // ─── Dispatcher boot ───────────────────────────────────────────────
    [Event(EvtDispatcherInitialized,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Notification dispatcher initialized")]
    public void DispatcherInitialized()
    {
        if (IsEnabled()) WriteEvent(EvtDispatcherInitialized);
    }

    [Event(EvtDispatcherInitializedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "channels={0} | channel_count={1}")]
    public void DispatcherInitializedDetail(string channels, int channel_count)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtDispatcherInitializedDetail, channels, channel_count);
    }

    // ─── Catalogue registration (Verbose-only: a boot audit trail) ─────
    [Event(EvtCatalogRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification_ids={0} | descriptor_count={1}")]
    public void CatalogRegistered(string notification_ids, int descriptor_count)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtCatalogRegistered, notification_ids, descriptor_count);
    }

    // ─── Show ──────────────────────────────────────────────────────────
    // The start of a prompt that waits on a person: it ends with exactly one of
    // NotificationResponded, NotificationUnanswered, NotificationCancelled or
    // NotificationFailed. notification_id (a catalog id, never the text shown)
    // pairs them in the file alone.
    [Event(EvtNotificationShown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "Showing a notification")]
    public void NotificationShown(string notification_id)
    {
        if (IsEnabled()) WriteEvent(EvtNotificationShown, notification_id);
    }

    [Event(EvtNotificationShownDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification_id={0} | channel={1} | severity={2}")]
    public void NotificationShownDetail(string notification_id, string channel, string severity)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationShownDetail, notification_id, channel, severity);
    }

    // ─── Responded ─────────────────────────────────────────────────────
    [Event(EvtNotificationResponded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "A notification was answered")]
    public void NotificationResponded(string notification_id)
    {
        if (IsEnabled()) WriteEvent(EvtNotificationResponded, notification_id);
    }

    [Event(EvtNotificationRespondedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification_id={0} | action_id={1} | has_text_input={2}")]
    public void NotificationRespondedDetail(string notification_id, string action_id, int has_text_input)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationRespondedDetail, notification_id, action_id, has_text_input);
    }

    // ─── Dropped ───────────────────────────────────────────────────────
    // The Warning carries the notification and the reason itself: the log
    // keeps Warnings whatever the verbosity, so a line that said only "dropped"
    // left a reader unable to tell which notification, or why. reason:
    // no_channel (none registered for the kind) or channel_unavailable.
    // Id 9 was the Verbose mirror of this line and is retired.
    [Event(EvtNotificationDropped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification was dropped because no channel could show it\nnotification_id={0} | reason={1}")]
    public void NotificationDropped(string notification_id, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtNotificationDropped, notification_id, reason);
    }

    // ─── Cancellation (Verbose-only) ───────────────────────────────────
    [Event(EvtPromptCancelled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification_id={0}")]
    public void PromptCancelled(string notification_id)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPromptCancelled, notification_id);
    }

    // ─── Cancelled (closes the step a caller cancelled) ────────────────
    [Event(EvtNotificationCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "A notification prompt was cancelled")]
    public void NotificationCancelled(string notification_id)
    {
        if (IsEnabled()) WriteEvent(EvtNotificationCancelled, notification_id);
    }

    // ─── Unanswered (the prompt ended without an answer) ───────────────
    [Event(EvtNotificationUnanswered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "A notification went unanswered")]
    public void NotificationUnanswered(string notification_id)
    {
        if (IsEnabled()) WriteEvent(EvtNotificationUnanswered, notification_id);
    }

    [Event(EvtNotificationUnansweredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification_id={0} | channel={1}")]
    public void NotificationUnansweredDetail(string notification_id, string channel)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationUnansweredDetail, notification_id, channel);
    }

    // ─── Failed (a channel threw while delivering) ─────────────────────
    // Id 18 was the Verbose mirror carrying the cause and is retired: the
    // cause belongs on the line the log keeps.
    [Event(EvtNotificationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification channel failed to deliver\nnotification_id={0} | ex_type={1} | ex_message={2}")]
    public void NotificationFailed(string notification_id, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNotificationFailed, notification_id, ex_type, ex_message);
    }

    // ─── Toast channel failure modes ───────────────────────────────────
    [Event(EvtToastsUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Toast notifications are unavailable in an elevated process")]
    public void ToastsUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtToastsUnavailable);
    }

    // Id 13 was the Verbose mirror carrying the cause and is retired.
    [Event(EvtToastRegistrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Toast notification registration failed\nex_type={0} | ex_message={1}")]
    public void ToastRegistrationFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtToastRegistrationFailed, ex_type, ex_message);
    }

    // A toast that a cancelled prompt asked to take back could not be removed
    // from the screen and the Notification Center. The degradation is one
    // incident: ToastWithdrawIncident writes this line once and the recovery
    // once, however many withdrawals fail in between.
    [Event(EvtToastWithdrawFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A toast could not be withdrawn and stays on screen\nex_type={0} | ex_message={1}")]
    public void ToastWithdrawFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtToastWithdrawFailed, ex_type, ex_message);
    }

    [Event(EvtToastWithdrawRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "Toast withdrawal works again")]
    public void ToastWithdrawRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtToastWithdrawRecovered);
    }
}
