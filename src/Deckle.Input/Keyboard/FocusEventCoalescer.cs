namespace Deckle.Input;

// Collapses the short WinEvent burst produced by one focus transition. Windows
// commonly reports both the foreground window and its focused object, and some
// accessibility providers repeat the same object-focus event. Publishing the
// first signal keeps the password gate synchronous; the bounded window avoids
// mistaking a later return to the same target for the original transition.
internal sealed class FocusEventCoalescer
{
    internal const uint WindowMilliseconds = 50;

    private bool _hasObserved;
    private uint _eventType;
    private IntPtr _window;
    private int _objectId;
    private int _childId;
    private uint _timestamp;

    public bool ShouldPublish(uint eventType, IntPtr window, int objectId, int childId, uint timestamp)
    {
        bool sameBurst = _hasObserved && unchecked(timestamp - _timestamp) <= WindowMilliseconds;
        bool sameTarget = window == _window && objectId == _objectId && childId == _childId;
        bool foregroundPair = window == _window &&
            _eventType == WinEventInterop.EVENT_SYSTEM_FOREGROUND &&
            eventType == WinEventInterop.EVENT_OBJECT_FOCUS;
        bool shouldPublish = !sameBurst || (!sameTarget && !foregroundPair);

        // Keep the last observed native target, including suppressed callbacks.
        // This makes foreground + initial focus one pair without hiding a second,
        // genuinely different object that follows in the same short interval.
        _hasObserved = true;
        _eventType = eventType;
        _window = window;
        _objectId = objectId;
        _childId = childId;
        _timestamp = timestamp;
        return shouldPublish;
    }
}
