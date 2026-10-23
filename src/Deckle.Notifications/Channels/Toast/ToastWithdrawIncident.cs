using System;

namespace Deckle.Notifications;

// Reports a toast that cannot be taken back once, and the withdrawal that works
// again once. A service that refuses removals would otherwise write a Warning
// for every prompt a caller abandons. Kept apart from ToastChannel because the
// channel cannot run without the platform's AppNotificationManager, and this
// rule can.
public sealed class ToastWithdrawIncident
{
    private readonly object _gate = new();
    private bool _failing;

    // Emitted under the lock so a failure and the recovery that follows it
    // reach the journal in the order they happened.
    public void Failed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        lock (_gate)
        {
            if (_failing) return;
            _failing = true;
            DeckleNotificationsSource.Log.ToastWithdrawFailed(ex.GetType().Name, ex.Message);
        }
    }

    public void Succeeded()
    {
        lock (_gate)
        {
            if (!_failing) return;
            _failing = false;
            DeckleNotificationsSource.Log.ToastWithdrawRecovered();
        }
    }
}
