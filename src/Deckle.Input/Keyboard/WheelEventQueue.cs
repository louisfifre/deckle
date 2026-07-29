namespace Deckle.Input;

// Fixed handoff from the low-level hook to the input pump. The hook writes one
// value and posts one message; correlation and subscriber delivery happen only
// after the callback has returned to Windows.
internal sealed class WheelEventQueue
{
    private const int Capacity = 256;
    private const int Mask = Capacity - 1;

    private readonly MouseWheelEvent[] _items = new MouseWheelEvent[Capacity];
    private int _read;
    private int _write;

    public bool TryEnqueue(in MouseWheelEvent wheelEvent)
    {
        int write = _write;
        if (write - Volatile.Read(ref _read) >= Capacity)
            return false;

        _items[write & Mask] = wheelEvent;
        Volatile.Write(ref _write, write + 1);
        return true;
    }

    public bool TryDequeue(out MouseWheelEvent wheelEvent)
    {
        int read = _read;
        if (read == Volatile.Read(ref _write))
        {
            wheelEvent = default;
            return false;
        }

        wheelEvent = _items[read & Mask];
        Volatile.Write(ref _read, read + 1);
        return true;
    }
}
