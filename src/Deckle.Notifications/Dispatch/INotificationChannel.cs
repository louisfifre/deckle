using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Notifications;

// A concrete delivery surface. The dispatcher selects a channel by
// descriptor.Channel and delegates the show and await to it. A channel resolves
// the descriptor's .resw keys, builds the platform notification, and completes
// the returned task when the user answers (or the prompt is cancelled).
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    // False when the channel cannot deliver in the current process state
    // (e.g. registration failed, or the process is elevated and Windows would
    // silently drop the toast). The dispatcher treats an unavailable channel as
    // a drop and returns null.
    bool IsAvailable { get; }

    // bodyArgs feed Loc.Format for a composite-format BodyKey. Cancellation
    // propagates as TaskCanceledException.
    //
    // Null means the prompt ended without an answer — e.g. the toast expired
    // unseen. A channel must always settle the returned task: it never leaks a
    // pending await for a prompt nobody acts on.
    Task<NotificationResponse?> PromptAsync(NotificationDescriptor descriptor, object?[]? bodyArgs, CancellationToken ct);
}
