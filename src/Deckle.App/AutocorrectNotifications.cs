using System.Collections.Generic;
using Deckle.Notifications;

namespace Deckle.App;

// Lives in the App so the autocorrect engine stays free of any Notifications
// dependency: it only raises EnrollmentSuggested. The descriptor Id is a stable
// public contract (point.snake_case, never renamed or recycled). The strings are
// .resw keys resolved through Loc, so they are mirrored in Deckle.App's
// Resources.resw.
public static class AutocorrectNotifications
{
    // Echoed back in NotificationResponse.ActionId.
    public const string EnableAction = "enable";
    public const string DeclineAction = "decline";

    // The body's {0} is the process name.
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
