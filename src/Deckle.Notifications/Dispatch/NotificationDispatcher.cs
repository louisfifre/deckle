using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Diagnostics;

namespace Deckle.Notifications;

// Composition root for the notification subsystem. Initialize wires the
// available channels once at boot; callers reach the singleton through
// Instance and ask the user something via PromptAsync. The dispatcher owns the
// catalogue, validates the descriptor is known, routes to the channel matching
// descriptor.Channel, and emits the observability narrative (shown / responded
// / dropped / cancelled / unanswered / failed). It holds no UI and no platform
// knowledge — that lives in the channels.
public sealed class NotificationDispatcher
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationChannel> _channels;

    // One dispatcher per process, set by Initialize at boot before any call
    // site asks.
    public static NotificationDispatcher? Instance { get; private set; }

    public NotificationCatalog Catalog { get; } = new();

    private NotificationDispatcher(INotificationChannel[] channels)
    {
        var map = new Dictionary<NotificationChannel, INotificationChannel>();
        foreach (var channel in channels)
        {
            // Last writer wins if two channels claim the same kind — a boot-time
            // wiring mistake, not a runtime case worth a throw here.
            map[channel.Channel] = channel;
        }
        _channels = map;
    }

    public static NotificationDispatcher Initialize(params INotificationChannel[] channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        var dispatcher = new NotificationDispatcher(channels);
        Instance = dispatcher;

        DeckleNotificationsSource.Log.DispatcherInitialized();
        if (DeckleNotificationsSource.Log.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
        {
            var names = new string[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                names[i] = channels[i].Channel.ToString();
            }
            DeckleNotificationsSource.Log.DispatcherInitializedDetail(string.Join(",", names), channels.Length);
        }

        return dispatcher;
    }

    // Whether a prompt for this descriptor would be shown right now. A caller
    // whose answer must tell "could not ask" from "nobody answered" checks this
    // first, since PromptAsync returns null for both. Read live, like the
    // channel's own availability.
    public bool CanPrompt(NotificationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Catalog.IsRegistered(descriptor.Id)
               && _channels.TryGetValue(descriptor.Channel, out var channel)
               && channel.IsAvailable;
    }

    // Returns null for two distinct no-answer cases the narrative tells apart:
    // the notification could not be shown (no channel for the requested kind, or
    // the channel is unavailable → dropped), or it was shown but never answered
    // (e.g. the toast expired unseen → unanswered). For enrollment-prompt
    // semantics where "ignoring is a valid answer", callers tolerate null in
    // both. An unregistered descriptor is a programmer error and throws.
    // Cancellation propagates as TaskCanceledException.
    public async Task<NotificationResponse?> PromptAsync(
        NotificationDescriptor descriptor,
        object?[]? bodyArgs = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!Catalog.IsRegistered(descriptor.Id))
        {
            throw new InvalidOperationException(
                $"Notification '{descriptor.Id}' is not registered in the catalog. Register it at boot before prompting.");
        }

        if (!_channels.TryGetValue(descriptor.Channel, out var channel) || !channel.IsAvailable)
        {
            DeckleNotificationsSource.Log.NotificationDropped();
            if (DeckleNotificationsSource.Log.IsEnabled())
            {
                var reason = channel is null ? "no_channel" : "channel_unavailable";
                DeckleNotificationsSource.Log.NotificationDroppedDetail(descriptor.Id, reason);
            }
            return null;
        }

        DeckleNotificationsSource.Log.NotificationShown();
        if (DeckleNotificationsSource.Log.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
        {
            DeckleNotificationsSource.Log.NotificationShownDetail(
                descriptor.Id, descriptor.Channel.ToString(), descriptor.Severity.ToString());
        }

        try
        {
            var response = await channel.PromptAsync(descriptor, bodyArgs, ct).ConfigureAwait(false);

            // No answer (e.g. the toast expired unseen): the step still closes
            // at Info.
            if (response is null)
            {
                DeckleNotificationsSource.Log.NotificationUnanswered();
                if (DeckleNotificationsSource.Log.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
                {
                    DeckleNotificationsSource.Log.NotificationUnansweredDetail(
                        descriptor.Id, descriptor.Channel.ToString());
                }
                return null;
            }

            DeckleNotificationsSource.Log.NotificationResponded();
            if (DeckleNotificationsSource.Log.IsEnabled())
            {
                DeckleNotificationsSource.Log.NotificationRespondedDetail(
                    descriptor.Id, response.ActionId, response.TextInput is null ? 0 : 1);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            // Closing Info for the step NotificationShown opened: cancellation
            // is caller-initiated and benign, so Info, not Warning.
            DeckleNotificationsSource.Log.NotificationCancelled();
            if (DeckleNotificationsSource.Log.IsEnabled())
            {
                DeckleNotificationsSource.Log.PromptCancelled(descriptor.Id);
            }
            throw;
        }
        catch (Exception ex)
        {
            // Without this catch the exception escapes with NotificationShown as
            // the last trace and the step never closes.
            DeckleNotificationsSource.Log.NotificationFailed();
            if (DeckleNotificationsSource.Log.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
            {
                DeckleNotificationsSource.Log.NotificationFailedDetail(descriptor.Id, ex.Message);
            }
            throw;
        }
    }
}
