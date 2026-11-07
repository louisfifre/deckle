using System.Collections.Generic;

using Deckle.Notifications;

namespace Deckle.Setup;

// Setup's slice of the notification catalogue — the silent update check's one
// user-facing prompt. Registered by the App at boot beside the other modules'
// descriptors; the string keys are mirrored into Deckle.App's Resources.resw
// (Loc reads the root map only).
public static class SetupNotifications
{
    public const string ViewActionId = "view";

    // The toast only opens the update window; installing is a separate,
    // explicit choice made there. A click on the toast body is a stray-click
    // risk (Windows raises it for any press on the toast), so it must never
    // do more than the "View update" button does.
    //
    // "Later", or ignoring the toast, leaves the offer parked in
    // UpdateService.Available, still reachable from Settings.
    public static bool ShouldOpenUpdateWindow(NotificationResponse? response) =>
        response is { ActionId: ViewActionId or NotificationResponse.BodyActionId };

    public static readonly NotificationDescriptor UpdateAvailable = new(
        Id: "setup.update_available",
        Category: "setup",
        TitleKey: "SetupNotifications_UpdateAvailable_Title",
        BodyKey: "SetupNotifications_UpdateAvailable_Body_Format",
        Severity: NotificationSeverity.Info,
        Channel: NotificationChannel.Toast,
        Actions: new[]
        {
            new NotificationAction(ViewActionId, "SetupNotifications_UpdateAvailable_View"),
            new NotificationAction("later", "SetupNotifications_UpdateAvailable_Later"),
        });

    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { UpdateAvailable };
}
