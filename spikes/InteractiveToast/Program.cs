using System.Security.Principal;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

// Spike scenario: the autocorrect enrollment prompt — Deckle observed a manual
// correction and asks whether to learn it, with an editable reply box.
// What must come out the other side: which button was clicked, and the text
// box content at click time.

if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.WriteLine("WARNING: elevated process — Windows silently drops toasts from elevated apps.");
}

var manager = AppNotificationManager.Default;
var invoked = new TaskCompletionSource<string>();

// Subscribed BEFORE Register(): otherwise Windows spawns a fresh process to
// deliver the activation instead of raising the event here.
manager.NotificationInvoked += (_, args) =>
{
    var action = args.Arguments.TryGetValue("action", out var a) ? a : "(body click)";
    var reply = args.UserInput.TryGetValue("replyBox", out var r) ? r : "(no input)";
    invoked.TrySetResult($"action={action}  replyBox=\"{reply}\"");
};

manager.Register();
Console.WriteLine("Registered (unpackaged, no MSIX).");

var toast = new AppNotificationBuilder()
    .AddArgument("action", "open")
    .AddText("Deckle learned a correction")
    .AddText("“teh” → “the” — add it to your dictionary?")
    .AddTextBox("replyBox", "Edit the correction", "Correction")
    .AddButton(new AppNotificationButton("Accept")
        .AddArgument("action", "accept")
        .SetInputId("replyBox"))   // pins the button beside the text box (inline reply)
    .AddButton(new AppNotificationButton("Decline")
        .AddArgument("action", "decline"))
    .BuildNotification();

manager.Show(toast);
Console.WriteLine("Toast shown. Interact with it — Accept (reads the text box), Decline, or click the body.");
Console.WriteLine("Waiting up to 120 s; a dismissed toast lands in the Notification Center and still works from there.");

var winner = await Task.WhenAny(invoked.Task, Task.Delay(TimeSpan.FromSeconds(120)));
Console.WriteLine(winner == invoked.Task
    ? $"Invoked: {invoked.Task.Result}"
    : "Timed out — nothing received. Check Notification Center, Do Not Disturb, and notification settings for this app.");

manager.Unregister();
