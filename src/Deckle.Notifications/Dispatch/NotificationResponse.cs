namespace Deckle.Notifications;

// What the user did with a notification. ActionId is the clicked button's
// Id, or BodyActionId when the user clicked the notification body itself.
// TextInput carries the inline field's value when the descriptor declared one
// and the user typed into it, otherwise null.
public sealed record NotificationResponse(string ActionId, string? TextInput)
{
    // Sentinel ActionId for a click on the notification body (no button).
    public const string BodyActionId = "body";
}
