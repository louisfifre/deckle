using System.Collections.Generic;

using Deckle.Notifications;

namespace Deckle.Setup;

// Setup's slice of the notification catalogue — the silent update check's one
// user-facing prompt. Registered by the App at boot beside the other modules'
// descriptors; the string keys are mirrored into Deckle.App's Resources.resw
// (Loc reads the root map only).
public static class SetupNotifications
{
    // "Later", or ignoring the toast, leaves the offer parked in
    // UpdateService.Available, still reachable from Settings.
    public static readonly NotificationDescriptor UpdateAvailable = new(
        Id: "setup.update_available",
        Category: "setup",
        TitleKey: "SetupNotifications_UpdateAvailable_Title",
        BodyKey: "SetupNotifications_UpdateAvailable_Body_Format",
        Severity: NotificationSeverity.Info,
        Channel: NotificationChannel.Toast,
        Actions: new[]
        {
            new NotificationAction("install", "SetupNotifications_UpdateAvailable_Install"),
            new NotificationAction("later", "SetupNotifications_UpdateAvailable_Later"),
        });

    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { UpdateAvailable };
}
