using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Catalog;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Deckle.Notifications;

// INotificationChannel for NotificationChannel.Toast. Builds a Windows toast
// from a descriptor, shows it through the AppNotificationManager, and completes
// when the user answers — correlated by the per-show nonce held in
// ToastActivation.
//
// Availability has three gates. (1) Registration must have succeeded. (2) The
// process must not be elevated: Windows silently drops toasts raised from an
// elevated app, so an elevated Deckle reports the channel unavailable and the
// dispatcher routes around it. The elevation check runs once at construction;
// the Warning is emitted once when detected. (3) The platform setting must be
// AppNotificationSetting.Enabled — read live, because the user can toggle
// notifications off at runtime, and Windows would then make Show succeed
// silently with nothing presented.
public sealed class ToastChannel : INotificationChannel
{
    // How long a shown prompt stays live before it settles itself as unanswered.
    // Module constant, not configurable: it bounds the await and the toast's own
    // Expiration so neither outlives the other.
    private static readonly TimeSpan PromptLifetime = TimeSpan.FromMinutes(15);

    private readonly ToastActivation _activation = new();
    private readonly bool _isElevated;

    public ToastChannel()
    {
        _isElevated = DetectElevated();
        if (_isElevated)
        {
            DeckleNotificationsSource.Log.ToastsUnavailable();
        }
    }

    public NotificationChannel Channel => NotificationChannel.Toast;

    // Registration succeeded AND the process is not elevated AND notifications
    // are enabled in Windows. The setting is read live (not cached) so a
    // runtime toggle is honored on the next prompt.
    public bool IsAvailable
        => _activation.RegistrationSucceeded
           && !_isElevated
           && _activation.Setting == AppNotificationSetting.Enabled;

    public Task<NotificationResponse?> PromptAsync(
        NotificationDescriptor descriptor,
        object?[]? bodyArgs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // An already-cancelled token must not Show a toast nobody awaits.
        ct.ThrowIfCancellationRequested();

        var title = Loc.Get(descriptor.TitleKey);
        var body = bodyArgs is { Length: > 0 }
            ? Loc.Format(descriptor.BodyKey, bodyArgs)
            : Loc.Get(descriptor.BodyKey);

        // Per-show correlation token. A toast carries only its baked-in
        // arguments, so the nonce is how the activation finds this exact show.
        var nonce = Guid.NewGuid().ToString("n");

        var builder = new AppNotificationBuilder()
            // Body click → BodyActionId. Every element also carries the nonce so
            // any activation routes back to this show.
            .AddArgument("action", NotificationResponse.BodyActionId)
            .AddArgument("nonce", nonce)
            .AddText(title)
            .AddText(body);

        if (descriptor.TextInput is { } input)
        {
            // Three-arg overload (id, placeholder, title); the descriptor carries
            // only a placeholder, so the title above the box is left empty.
            builder.AddTextBox(input.Id, Loc.Get(input.PlaceholderKey), string.Empty);
        }

        for (int i = 0; i < descriptor.Actions.Count; i++)
        {
            var action = descriptor.Actions[i];
            var button = new AppNotificationButton(Loc.Get(action.LabelKey))
                .AddArgument("action", action.Id)
                .AddArgument("nonce", nonce);

            // v1 simplification: when the descriptor has an inline text box, we
            // pin it beside the FIRST action's button only (the Windows
            // inline-reply pattern). A richer mapping — choosing which button
            // owns the input — is deferred.
            if (descriptor.TextInput is { } ti && i == 0)
            {
                button.SetInputId(ti.Id);
            }

            builder.AddButton(button);
        }

        var notification = builder.BuildNotification();

        // Bound the toast's own life so a prompt nobody answers does not linger
        // in the Notification Center past the window we still await it for. The
        // internal expiry below settles the await on the same horizon.
        notification.Expiration = DateTimeOffset.Now + PromptLifetime;

        var tcs = new TaskCompletionSource<NotificationResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register the pending TCS BEFORE Show, so an activation that races the
        // show still finds its target.
        _activation.RegisterPending(nonce, tcs);

        // Cancellation drops the pending entry and cancels the awaiter. The
        // already-shown toast stays in the Notification Center but its
        // activation will be an orphan (no pending entry) and is ignored.
        // The registration is disposed once the task settles, so a long-lived
        // token does not retain the closure past the prompt's life.
        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(() =>
            {
                _activation.RemovePending(nonce);
                tcs.TrySetCanceled(ct);
            });
            _ = tcs.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // The prompt MUST always settle. AppNotificationManager raises
        // NotificationInvoked only on user interaction — there is no dismiss,
        // expiry, or timeout event — so a toast nobody clicks would leave this
        // await and its pending entry hanging for process life. We settle it
        // ourselves: a CTS fired after PromptLifetime drops the pending entry
        // and completes the prompt as unanswered (null). Disposed via a
        // continuation when the task settles, mirroring the ct registration
        // above, so nothing outlives the prompt.
        var expiry = new CancellationTokenSource(PromptLifetime);
        expiry.Token.Register(() =>
        {
            _activation.RemovePending(nonce);
            tcs.TrySetResult(null);
        });
        _ = tcs.Task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            expiry,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _activation.Manager.Show(notification);

        return tcs.Task;
    }

    // WindowsIdentity/WindowsPrincipal admin-role check, matching the spike.
    private static bool DetectElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
