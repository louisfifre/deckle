namespace Deckle.Input;

// Owns the delivery contract shared by the native mouse sources. Hook button
// signals are queued so the low-level callback stays bounded; Raw Input publishes
// only when the hook is unavailable. Wheel messages retain their own event path.
internal sealed class MouseInteractionRouter
{
    private readonly Action _queuePointer;
    private readonly Action _publishPointer;
    private readonly Func<WheelAxis, short, LowLevelMouseHookInterop.MSLLHOOKSTRUCT, bool> _publishWheel;

    public MouseInteractionRouter(
        Action queuePointer,
        Action publishPointer,
        Func<WheelAxis, short, LowLevelMouseHookInterop.MSLLHOOKSTRUCT, bool> publishWheel)
    {
        _queuePointer = queuePointer;
        _publishPointer = publishPointer;
        _publishWheel = publishWheel;
    }

    public bool ObserveHookMessage(
        int message,
        LowLevelMouseHookInterop.MSLLHOOKSTRUCT hook)
    {
        if (LowLevelMouseHookInterop.IsButtonDown(message))
        {
            _queuePointer();
            return false;
        }

        if (message == LowLevelMouseHookInterop.WM_MOUSEWHEEL)
            return _publishWheel(
                WheelAxis.Vertical,
                LowLevelMouseHookInterop.GetWheelDelta(hook.mouseData),
                hook);
        else if (message == LowLevelMouseHookInterop.WM_MOUSEHWHEEL)
            return _publishWheel(
                WheelAxis.Horizontal,
                LowLevelMouseHookInterop.GetWheelDelta(hook.mouseData),
                hook);

        return false;
    }

    public void ObserveRawButtonDown(bool hookInstalled)
    {
        if (!hookInstalled)
            _publishPointer();
    }

    public void PublishQueuedButtonDown() => _publishPointer();
}
