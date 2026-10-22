namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private const uint WheelObservationTimerId = 1;
    private const uint WheelObservationTimerMs = 10;

    // Runs inside the hook callback: losses are only counted here and reported
    // from the pump by EvaluateRelayHealth.
    private bool QueueHookWheelObservation(in MouseWheelEvent wheelEvent)
    {
        if (!_hookWheelEvents.TryEnqueue(in wheelEvent))
        {
            _hookQueueDrops++;
            return false;
        }

        if (RawInputInterop.PostThreadMessage(
                _threadId,
                WM_APP_WHEEL_OBSERVATION,
                IntPtr.Zero,
                IntPtr.Zero))
            return true;

        NotePostFailure();
        return false;
    }

    private void PublishQueuedHookWheels()
    {
        bool sawHookWheel = false;
        while (_hookWheelEvents.TryDequeue(out MouseWheelEvent wheelEvent))
        {
            sawHookWheel = true;
            ObserveWheel(in wheelEvent);
        }

        double nowMs = RawInputHost.NowMs;
        if (sawHookWheel)
            NoteHookWheelSeen(nowMs);
        EvaluateRelayHealth(nowMs);
    }

    private void ObserveWheel(in MouseWheelEvent wheelEvent)
    {
        if (_wheelObservations.Observe(in wheelEvent, out MouseWheelEvent publish))
            WheelObserved?.Invoke(publish);

        if (_wheelObservations.HasPending && !_wheelObservationTimerScheduled)
            ScheduleWheelObservationTimer();
    }

    private void FlushExpiredWheelObservations()
    {
        CancelWheelObservationTimer();
        double nowMs = RawInputHost.NowMs;
        while (_wheelObservations.TryDequeueExpired(nowMs, out MouseWheelEvent wheelEvent))
        {
            if (wheelEvent.Source == WheelEventSource.RawInput && !wheelEvent.IsInjected)
                NoteUnpairedRawWheel(nowMs);
            WheelObserved?.Invoke(wheelEvent);
        }

        if (_wheelObservations.HasPending)
            ScheduleWheelObservationTimer();
    }

    // Without the timer the buffer cannot wait for a partner, so it flushes at
    // once: observers may then see one transition from both sources.
    private void ScheduleWheelObservationTimer()
    {
        _wheelObservationTimerScheduled = RawInputInterop.SetTimer(
            _hwnd,
            WheelObservationTimerId,
            WheelObservationTimerMs,
            IntPtr.Zero) != 0;
        if (_wheelObservationTimerScheduled)
            return;

        NoteTimerFailure();
        FlushBufferedWheelObservations();
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
