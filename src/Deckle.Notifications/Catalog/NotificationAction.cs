namespace Deckle.Notifications;

// A button on a notification. Id is the stable token echoed back in the
// NotificationResponse.ActionId when the user clicks it; LabelKey is the .resw
// key the channel resolves to the visible label.
public sealed record NotificationAction(string Id, string LabelKey);

// An inline text field on a notification. Id is the stable token the channel
// uses to read the typed value back into NotificationResponse.TextInput;
// PlaceholderKey is the .resw key for the empty-field hint.
public sealed record NotificationTextInput(string Id, string PlaceholderKey);
