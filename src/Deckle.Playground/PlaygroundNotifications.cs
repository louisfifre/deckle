using System.Collections.Generic;
using Deckle.Notifications;

namespace Deckle.Playground;

// Playground's slice of the notification catalogue. Each Deckle module owns
// the descriptors for the user messages it can raise; the App indexes them
// centrally at boot via NotificationDispatcher.Instance.Catalog.Register.
// This is the Playground's manual test surface: one interactive descriptor
// exercised by HomePage's "Send test prompt" button so the toast channel can
// be validated by hand (buttons + reply box) before any business module
// depends on it.
//
// The descriptor Id is a public contract from day one (point.snake_case,
// stable across renames so user opt-outs and audit traces survive). The
// Title/Body/label/placeholder strings are .resw keys, resolved by the
// channel through Deckle.Catalog.Loc — never hardcoded here.
public static class PlaygroundNotifications
{
    // Interactive enrollment-style prompt: two actions plus a free-text
    // reply. Severity Info — it carries no failure, it's a manual probe.
    // Channel Toast is a preference (the immediate need is native Windows 11
    // interactive toasts), not a command; the dispatcher has the final say.
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

    // The Playground's contribution to the central catalogue. App registers
    // this list at boot; a future Settings "Notifications" page enumerates
    // the global index built from every module's All.
    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { TestPrompt };
}
