using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;

namespace Deckle.Notifications;

// Owns the AppNotificationManager wiring and the correlation between a shown
// toast and the activation that comes back when the user interacts with it.
//
// Two non-negotiable traps, both proven by spikes/InteractiveToast/Program.cs:
//
//   1. NotificationInvoked MUST be subscribed BEFORE Register(). If Register
//      runs first, Windows treats the activation as a cold launch and spawns a
//      fresh process to deliver it, instead of raising the event in this
//      already-running process.
//
//   2. The activation carries only the arguments baked into the toast at build
//      time. We thread a per-show nonce ("pid") through every AddArgument so
//      the handler can route the activation back to the exact TaskCompletionSource
//      that the show is awaiting — Windows gives no other correlation token.
//
// Registration is wrapped in try/catch: a failure (e.g. unsupported OS state)
// marks the channel unavailable rather than crashing boot.
internal sealed class ToastActivation
{
    private readonly AppNotificationManager _manager = AppNotificationManager.Default;

    // Pending prompts keyed by the per-show nonce. RunContinuationsAsynchronously
    // so the awaiting PromptAsync continuation never runs inline on the
    // AppNotificationManager callback thread.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<NotificationResponse>> _pending = new();

    // True once Register() succeeded. The channel folds this into IsAvailable.
    public bool RegistrationSucceeded { get; private set; }

    public ToastActivation()
    {
        // Handler first — see trap 1.
        _manager.NotificationInvoked += OnNotificationInvoked;

        try
        {
            _manager.Register();
            RegistrationSucceeded = true;
        }
        catch (Exception ex)
        {
            RegistrationSucceeded = false;
            DeckleNotificationsSource.Log.ToastRegistrationFailed();
            if (DeckleNotificationsSource.Log.IsEnabled())
            {
                DeckleNotificationsSource.Log.ToastRegistrationFailedDetail(ex.Message);
            }
        }
    }

    public AppNotificationManager Manager => _manager;

    // Registers the awaiting TCS under its nonce before the toast is shown, so
    // an activation that races the Show call still finds its target.
    public void RegisterPending(string nonce, TaskCompletionSource<NotificationResponse> tcs)
        => _pending[nonce] = tcs;

    // Drops a pending entry without completing it — used on cancellation.
    public void RemovePending(string nonce)
        => _pending.TryRemove(nonce, out _);

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // The nonce ("pid") routes the activation to its awaiting show. An
        // activation with no nonce (or an unknown one) is an orphan — a cold
        // activation after process exit, deliberately out of scope for v1.
        if (!args.Arguments.TryGetValue("pid", out var nonce) || string.IsNullOrEmpty(nonce))
        {
            return;
        }
        if (!_pending.TryRemove(nonce, out var tcs))
        {
            return;
        }

        // "action" is the clicked button's Id, or the body sentinel when the
        // user clicked the toast body. UserInput carries the inline text box
        // value keyed by the descriptor's TextInput.Id.
        var actionId = args.Arguments.TryGetValue("action", out var a) && !string.IsNullOrEmpty(a)
            ? a
            : NotificationResponse.BodyActionId;

        string? textInput = null;
        if (args.UserInput.Count > 0)
        {
            // A descriptor declares at most one text box in v1; take the first
            // value present rather than assuming a key we would have to thread
            // here.
            foreach (var pair in args.UserInput)
            {
                textInput = pair.Value;
                break;
            }
        }

        tcs.TrySetResult(new NotificationResponse(actionId, textInput));
    }
}
