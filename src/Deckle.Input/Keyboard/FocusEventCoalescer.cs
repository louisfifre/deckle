namespace Deckle.Input;

// Collapses exact WinEvent duplicates produced by accessibility providers.
// Foreground and object-focus are distinct observations even when they arrive
// for one window in one burst: the first keeps the password gate synchronous,
// while the second lets consumers probe the object that actually received
// keyboard focus.
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
        bool sameTarget = eventType == _eventType &&
            window == _window && objectId == _objectId && childId == _childId;
        bool shouldPublish = !sameBurst || !sameTarget;

        // Keep the last observed native target, including suppressed callbacks,
        // so only a consecutive exact duplicate disappears.
        _hasObserved = true;
        _eventType = eventType;
        _window = window;
        _objectId = objectId;
        _childId = childId;
        _timestamp = timestamp;
        return shouldPublish;
    }
}
