using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Catalog;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Deckle.Notifications;

// Builds a Windows toast from a descriptor, shows it through the
// AppNotificationManager, and completes when the user answers, correlated by
// the per-show nonce held in ToastActivation.
//
// Availability has three gates. (1) Registration must have succeeded. (2) The
// process must not be elevated: Windows silently drops toasts raised from an
// elevated app, so an elevated Deckle reports the channel unavailable and the
// dispatcher routes around it. (3) The platform setting must be
// AppNotificationSetting.Enabled, read live: the user can toggle notifications
// off at runtime, and Windows would then make Show succeed silently with
// nothing presented.
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

        var nonce = Guid.NewGuid().ToString("n");

        var builder = new AppNotificationBuilder()
            // Every element carries the nonce, or its activation is an orphan.
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

            // The inline text box pins beside the FIRST action's button only
            // (the Windows inline-reply pattern).
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

        // Tagged with the nonce so a prompt its caller abandons can be taken
        // back from the screen and the Notification Center (below): a stale
        // toast still offering a choice nobody awaits would mislead.
        notification.Tag = nonce;

        var tcs = new TaskCompletionSource<NotificationResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register the pending TCS BEFORE Show, so an activation that races the
        // show still finds its target.
        _activation.RegisterPending(nonce, tcs);

        // The already-shown toast is withdrawn; should the removal fail, its
        // activation is an orphan (no pending entry) and is ignored. The
        // registration is disposed once the task settles, so a long-lived
        // token does not retain the closure past the prompt's life.
        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(() =>
            {
                _activation.RemovePending(nonce);
                tcs.TrySetCanceled(ct);
                _ = WithdrawAsync(nonce);
            });
            _ = tcs.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // The prompt MUST always settle. AppNotificationManager raises
        // NotificationInvoked only on user interaction (no dismiss, expiry or
        // timeout event), so a toast nobody clicks would leave this await and
        // its pending entry hanging for process life. A CTS fired after
        // PromptLifetime drops the pending entry and completes the prompt as
        // unanswered (null); it is disposed when the task settles.
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

    // Best effort: the orphaned activation is already ignored, so a failed
    // removal only leaves a toast whose buttons do nothing.
    private async Task WithdrawAsync(string nonce)
    {
        try
        {
            await _activation.Manager.RemoveByTagAsync(nonce);
        }
        catch (Exception)
        {
        }
    }

    private static bool DetectElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
