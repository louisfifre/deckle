namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private const uint WheelObservationTimerId = 1;
    private const uint WheelObservationTimerMs = 10;

    private bool QueueHookWheelObservation(in MouseWheelEvent wheelEvent)
    {
        if (!_hookWheelEvents.TryEnqueue(in wheelEvent))
            return false;

        return RawInputInterop.PostThreadMessage(
            _threadId,
            WM_APP_WHEEL_OBSERVATION,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private void PublishQueuedHookWheels()
    {
        while (_hookWheelEvents.TryDequeue(out MouseWheelEvent wheelEvent))
            ObserveWheel(in wheelEvent);
    }

    private void ObserveWheel(in MouseWheelEvent wheelEvent)
    {
        if (_wheelObservations.Observe(in wheelEvent, out MouseWheelEvent publish))
            WheelObserved?.Invoke(publish);

        if (_wheelObservations.HasPending && !_wheelObservationTimerScheduled)
        {
            _wheelObservationTimerScheduled = RawInputInterop.SetTimer(
                _hwnd,
                WheelObservationTimerId,
                WheelObservationTimerMs,
                IntPtr.Zero) != 0;
            if (!_wheelObservationTimerScheduled)
                FlushBufferedWheelObservations();
        }
    }

    private void FlushExpiredWheelObservations()
    {
        CancelWheelObservationTimer();
        double nowMs = RawInputHost.NowMs;
        while (_wheelObservations.TryDequeueExpired(nowMs, out MouseWheelEvent wheelEvent))
            WheelObserved?.Invoke(wheelEvent);

        if (_wheelObservations.HasPending)
        {
            _wheelObservationTimerScheduled = RawInputInterop.SetTimer(
                _hwnd,
                WheelObservationTimerId,
                WheelObservationTimerMs,
                IntPtr.Zero) != 0;
            if (!_wheelObservationTimerScheduled)
                FlushBufferedWheelObservations();
        }
    }

    private void FlushAllWheelObservations()
    {
        CancelWheelObservationTimer();
        PublishQueuedHookWheels();
        FlushBufferedWheelObservations();
    }

    private void FlushBufferedWheelObservations()
    {
        while (_wheelObservations.TryDequeue(out MouseWheelEvent wheelEvent))
            WheelObserved?.Invoke(wheelEvent);
    }

    private void CancelWheelObservationTimer()
    {
        if (!_wheelObservationTimerScheduled)
            return;

        RawInputInterop.KillTimer(_hwnd, WheelObservationTimerId);
        _wheelObservationTimerScheduled = false;
    }
}
