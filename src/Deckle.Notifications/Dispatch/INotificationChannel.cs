using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Notifications;

// A concrete delivery surface — today only the Windows toast. The dispatcher
// selects a channel by descriptor.Channel and delegates the actual show +
// await to it. A channel is responsible for resolving the descriptor's .resw
// keys, building the platform notification, and completing the returned task
// when the user answers (or the prompt is cancelled).
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    // False when the channel cannot deliver in the current process state
    // (e.g. registration failed, or the process is elevated and Windows would
    // silently drop the toast). The dispatcher treats an unavailable channel as
    // a drop and returns null.
    bool IsAvailable { get; }

    // Shows the notification and completes when the user answers. bodyArgs feed
    // Loc.Format for a composite-format BodyKey. Cancellation propagates as
    // TaskCanceledException.
    Task<NotificationResponse> PromptAsync(NotificationDescriptor descriptor, object?[]? bodyArgs, CancellationToken ct);
}
