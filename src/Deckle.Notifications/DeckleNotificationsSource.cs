using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Notifications;

// EventSource provider for the Deckle.Notifications module. Observes the
// notification lifecycle — dispatcher boot, catalogue registration, and the
// show / responded / dropped / cancelled / unanswered / failed path of each
// prompt — plus the toast channel's two failure modes (elevated process,
// registration failure).
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info is a short
// Capital sentence with no IDs and no k=v; the technical detail (notification
// id, channel, action) lives in a Verbose mirror that FOLLOWS the Info.
// Notifications are outputs sent to the world, hence the transverse `Push`
// keyword (bit 3) on every event.
[EventSource(Name = "Deckle-Notifications")]
public sealed class DeckleNotificationsSource : DeckleEventSource
{
    public static readonly DeckleNotificationsSource Log = new();

    private DeckleNotificationsSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // Sequential from 1. IDs are public in the ETW manifest; do not reuse an
    // ID after deleting an event.
    public const int EvtDispatcherInitialized       = 1;
    public const int EvtDispatcherInitializedDetail = 2;
    public const int EvtCatalogRegistered           = 3;
    public const int EvtNotificationShown           = 4;
    public const int EvtNotificationShownDetail     = 5;
    public const int EvtNotificationResponded       = 6;
    public const int EvtNotificationRespondedDetail = 7;
    public const int EvtNotificationDropped         = 8;
    public const int EvtNotificationDroppedDetail   = 9;
    public const int EvtPromptCancelled             = 10;
    public const int EvtToastsUnavailable           = 11;
    public const int EvtToastRegistrationFailed     = 12;
    public const int EvtToastRegistrationFailedDetail = 13;
    public const int EvtNotificationCancelled       = 14;
    public const int EvtNotificationUnanswered      = 15;
    public const int EvtNotificationUnansweredDetail = 16;
    public const int EvtNotificationFailed          = 17;
    public const int EvtNotificationFailedDetail    = 18;

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
           Message = "dispatcher init | channels={0} | channel_count={1}")]
    public void DispatcherInitializedDetail(string channels, int channel_count)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtDispatcherInitializedDetail, channels, channel_count);
    }

    // ─── Catalogue registration (Verbose-only: a boot audit trail) ─────
    [Event(EvtCatalogRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "catalog registered | notification_ids={0} | descriptor_count={1}")]
    public void CatalogRegistered(string notification_ids, int descriptor_count)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtCatalogRegistered, notification_ids, descriptor_count);
    }

    // ─── Show ──────────────────────────────────────────────────────────
    [Event(EvtNotificationShown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Showing a notification")]
    public void NotificationShown()
    {
        if (IsEnabled()) WriteEvent(EvtNotificationShown);
    }

    [Event(EvtNotificationShownDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification shown | notification_id={0} | channel={1} | severity={2}")]
    public void NotificationShownDetail(string notification_id, string channel, string severity)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationShownDetail, notification_id, channel, severity);
    }

    // ─── Responded ─────────────────────────────────────────────────────
    [Event(EvtNotificationResponded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification was answered")]
    public void NotificationResponded()
    {
        if (IsEnabled()) WriteEvent(EvtNotificationResponded);
    }

    [Event(EvtNotificationRespondedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification responded | notification_id={0} | action_id={1} | has_text_input={2}")]
    public void NotificationRespondedDetail(string notification_id, string action_id, int has_text_input)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationRespondedDetail, notification_id, action_id, has_text_input);
    }

    // ─── Dropped ───────────────────────────────────────────────────────
    [Event(EvtNotificationDropped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification was dropped because no channel could show it")]
    public void NotificationDropped()
    {
        if (IsEnabled()) WriteEvent(EvtNotificationDropped);
    }

    [Event(EvtNotificationDroppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification dropped | notification_id={0} | reason={1}")]
    public void NotificationDroppedDetail(string notification_id, string reason)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationDroppedDetail, notification_id, reason);
    }

    // ─── Cancellation (Verbose-only) ───────────────────────────────────
    [Event(EvtPromptCancelled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "prompt cancelled | notification_id={0}")]
    public void PromptCancelled(string notification_id)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPromptCancelled, notification_id);
    }

    // ─── Cancelled (closes the step a caller cancelled) ────────────────
    [Event(EvtNotificationCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification prompt was cancelled")]
    public void NotificationCancelled()
    {
        if (IsEnabled()) WriteEvent(EvtNotificationCancelled);
    }

    // ─── Unanswered (the prompt ended without an answer) ───────────────
    [Event(EvtNotificationUnanswered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification went unanswered")]
    public void NotificationUnanswered()
    {
        if (IsEnabled()) WriteEvent(EvtNotificationUnanswered);
    }

    [Event(EvtNotificationUnansweredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification unanswered | notification_id={0} | channel={1}")]
    public void NotificationUnansweredDetail(string notification_id, string channel)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationUnansweredDetail, notification_id, channel);
    }

    // ─── Failed (a channel threw while delivering) ─────────────────────
    [Event(EvtNotificationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A notification channel failed to deliver")]
    public void NotificationFailed()
    {
        if (IsEnabled()) WriteEvent(EvtNotificationFailed);
    }

    [Event(EvtNotificationFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "notification failed | notification_id={0} | error={1}")]
    public void NotificationFailedDetail(string notification_id, string error)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtNotificationFailedDetail, notification_id, error);
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

    [Event(EvtToastRegistrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Toast notification registration failed")]
    public void ToastRegistrationFailed()
    {
        if (IsEnabled()) WriteEvent(EvtToastRegistrationFailed);
    }

    [Event(EvtToastRegistrationFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "toast registration failed | error={0}")]
    public void ToastRegistrationFailedDetail(string error)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtToastRegistrationFailedDetail, error);
    }
}
