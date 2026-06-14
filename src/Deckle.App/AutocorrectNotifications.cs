using System.Collections.Generic;
using Deckle.Notifications;

namespace Deckle.App;

// Autocorrect's slice of the notification catalogue. It lives in the App, not in
// the autocorrect engine library, on purpose: the engine stays free of any
// Notifications dependency — it only raises EnrollmentSuggested as a plain event
// — and the App, the composition root, turns that signal into a user prompt.
//
// The descriptor Id is a stable public contract (point.snake_case, never renamed
// or recycled). The Title/Body/label strings are .resw keys resolved by the toast
// channel through Loc; they are mirrored in Deckle.App's Resources.resw.
public static class AutocorrectNotifications
{
    // Action ids — echoed back in NotificationResponse.ActionId.
    public const string EnableAction = "enable";
    public const string DeclineAction = "decline";

    // Reactive enrollment offer: a correction would have applied in an app the
    // user has never decided on. The body carries {0} = the process name. Two
    // actions, no text input; ignoring the toast is a valid "not now" — the app
    // stays undecided and is offered again next run.
    public static readonly NotificationDescriptor Enroll = new(
        Id: "autocorrect.enroll",
        Category: "autocorrect",
        TitleKey: "AutocorrectNotifications_Enroll_Title",
        BodyKey: "AutocorrectNotifications_Enroll_Body",
        Severity: NotificationSeverity.Info,
        Channel: NotificationChannel.Toast,
        Actions: new[]
        {
            new NotificationAction(EnableAction, "AutocorrectNotifications_Enroll_Enable"),
            new NotificationAction(DeclineAction, "AutocorrectNotifications_Enroll_Decline"),
        });

    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { Enroll };
}
