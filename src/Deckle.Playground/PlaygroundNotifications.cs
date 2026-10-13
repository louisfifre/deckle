using System.Collections.Generic;
using Deckle.Notifications;

namespace Deckle.Playground;

// Playground's slice of the notification catalogue: one interactive
// descriptor, fired by HomePage's "Send test prompt" button to check the
// toast channel (buttons and reply box) by hand.
//
// The descriptor Id is a public contract: keep it stable across renames. The
// Title/Body/label/placeholder strings are .resw keys, resolved by the
// channel through Deckle.Catalog.Loc, never hardcoded here.
public static class PlaygroundNotifications
{
    // Channel Toast is a preference, not a command; the dispatcher has the
    // final say.
    public static readonly NotificationDescriptor TestPrompt = new(
        Id: "playground.test_prompt",
        Category: "playground",
        TitleKey: "PlaygroundNotifications_TestPrompt_Title",
        BodyKey: "PlaygroundNotifications_TestPrompt_Body",
        Severity: NotificationSeverity.Info,
        Channel: NotificationChannel.Toast,
        Actions: new[]
        {
            new NotificationAction("accept", "PlaygroundNotifications_TestPrompt_Accept"),
            new NotificationAction("decline", "PlaygroundNotifications_TestPrompt_Decline"),
        },
        TextInput: new NotificationTextInput("replyBox", "PlaygroundNotifications_TestPrompt_Placeholder"));

    // Registered by the App at boot.
    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { TestPrompt };
}
