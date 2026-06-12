using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Notifications;

namespace Deckle.Notifications.Tests;

// Test double for INotificationChannel: records the prompts it receives and
// returns a response the test controls. Stays a behavioral stand-in for a real
// channel — it never touches the platform, so the dispatcher's routing,
// availability handling, and cancellation can be exercised without a toast.
internal sealed class FakeNotificationChannel : INotificationChannel
{
    private readonly NotificationResponse _cannedResponse;

    public FakeNotificationChannel(
        NotificationChannel channel = NotificationChannel.Toast,
        bool isAvailable = true,
        NotificationResponse? cannedResponse = null)
    {
        Channel = channel;
        IsAvailable = isAvailable;
        _cannedResponse = cannedResponse ?? new NotificationResponse("ok", null);
    }

    public NotificationChannel Channel { get; }

    public bool IsAvailable { get; set; }

    // Every prompt the dispatcher routed here, in order.
    public List<PromptCall> Calls { get; } = new();

    // When set, PromptAsync awaits this before returning — lets a test observe
    // cancellation propagating through the channel boundary.
    public TaskCompletionSource<NotificationResponse>? PendingCompletion { get; set; }

    public async Task<NotificationResponse> PromptAsync(
        NotificationDescriptor descriptor, object?[]? bodyArgs, CancellationToken ct)
    {
        Calls.Add(new PromptCall(descriptor, bodyArgs, ct));

        if (PendingCompletion is not null)
        {
            // Honour cancellation while parked on the pending completion, the way
            // a real channel awaiting the user's answer would.
            using (ct.Register(() => PendingCompletion.TrySetCanceled(ct)))
            {
                return await PendingCompletion.Task.ConfigureAwait(false);
            }
        }

        ct.ThrowIfCancellationRequested();
        return _cannedResponse;
    }

    internal sealed record PromptCall(
        NotificationDescriptor Descriptor, object?[]? BodyArgs, CancellationToken Ct);
}
