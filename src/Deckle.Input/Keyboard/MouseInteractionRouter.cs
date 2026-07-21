namespace Deckle.Input;

// Owns the delivery contract shared by the native mouse sources. Hook button
// signals are queued so the low-level callback stays bounded; Raw Input publishes
// only when the hook is unavailable. Wheel messages retain their own event path.
internal sealed class MouseInteractionRouter
{
    private readonly Action _queuePointer;
    private readonly Action _publishPointer;
    private readonly Action<WheelAxis, short> _publishWheel;

    public MouseInteractionRouter(
        Action queuePointer,
        Action publishPointer,
        Action<WheelAxis, short> publishWheel)
    {
        _queuePointer = queuePointer;
        _publishPointer = publishPointer;
        _publishWheel = publishWheel;
    }

    public void ObserveHookMessage(int message, uint mouseData)
    {
        if (LowLevelMouseHookInterop.IsButtonDown(message))
        {
            _queuePointer();
            return;
        }

        if (message == LowLevelMouseHookInterop.WM_MOUSEWHEEL)
            _publishWheel(WheelAxis.Vertical, LowLevelMouseHookInterop.GetWheelDelta(mouseData));
        else if (message == LowLevelMouseHookInterop.WM_MOUSEHWHEEL)
            _publishWheel(WheelAxis.Horizontal, LowLevelMouseHookInterop.GetWheelDelta(mouseData));
    }

    public void ObserveRawButtonDown(bool hookInstalled)
    {
        if (!hookInstalled)
            _publishPointer();
    }

    public void PublishQueuedButtonDown() => _publishPointer();
}
