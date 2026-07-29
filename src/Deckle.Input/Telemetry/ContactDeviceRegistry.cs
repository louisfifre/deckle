namespace Deckle.Input;

internal readonly record struct RegisteredTouchpad(
    int Index,
    TouchpadCapabilities Capabilities);

// Device arrival and capture arming occur on different threads. This registry
// remembers arrivals before a session begins, then assigns stable session-local
// identities under the recorder's existing lock.
internal sealed class ContactDeviceRegistry
{
    private readonly Dictionary<IntPtr, TouchpadDevice> _known = [];
    private readonly Dictionary<IntPtr, RegisteredTouchpad> _session = [];
    private int _nextIndex;
    private bool _sessionActive;

    public IEnumerable<RegisteredTouchpad> SessionDevices => _session.Values;

    public void StartSession(IReadOnlyList<TouchpadDevice> snapshot)
    {
        foreach (TouchpadDevice touchpad in snapshot)
            _known[touchpad.Handle] = touchpad;

        _session.Clear();
        _nextIndex = 0;
        foreach (TouchpadDevice touchpad in _known.Values.OrderBy(
                     device => device.Handle.ToInt64()))
        {
            _session.Add(
                touchpad.Handle,
                new RegisteredTouchpad(_nextIndex++, touchpad.Capabilities));
        }

        _sessionActive = true;
    }

    public void EndSession()
    {
        _sessionActive = false;
        _session.Clear();
        _known.Clear();
        _nextIndex = 0;
    }

    public bool Observe(
        TouchpadDevice touchpad,
        bool preservePreviousIdentity,
        out RegisteredTouchpad registered)
    {
        _known[touchpad.Handle] = touchpad;
        if (!_sessionActive)
        {
            registered = default;
            return false;
        }

        if (_session.TryGetValue(touchpad.Handle, out RegisteredTouchpad existing))
        {
            if (existing.Capabilities == touchpad.Capabilities)
            {
                registered = existing;
                return false;
            }

            registered = preservePreviousIdentity
                ? new RegisteredTouchpad(_nextIndex++, touchpad.Capabilities)
                : existing with { Capabilities = touchpad.Capabilities };
            _session[touchpad.Handle] = registered;
            return preservePreviousIdentity;
        }

        registered = new RegisteredTouchpad(_nextIndex++, touchpad.Capabilities);
        _session.Add(touchpad.Handle, registered);
        return true;
    }

    public bool TryGet(IntPtr handle, out RegisteredTouchpad registered) =>
        _session.TryGetValue(handle, out registered);
}
