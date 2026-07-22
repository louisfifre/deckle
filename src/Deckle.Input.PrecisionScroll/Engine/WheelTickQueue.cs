namespace Deckle.Input.PrecisionScroll;

internal readonly record struct WheelTick(int Direction, double TimestampMs);

// Single-producer/single-consumer ring. The low-level hook only writes and the
// injection worker only reads, so volatile publication is sufficient and no
// lock or per-event allocation enters the hook path.
internal sealed class WheelTickQueue
{
    private const int Capacity = 64;
    private const int Mask = Capacity - 1;

    private readonly WheelTick[] _items = new WheelTick[Capacity];
    private int _read;
    private int _write;

    public bool TryEnqueue(WheelTick tick)
    {
        int write = _write;
        if (write - Volatile.Read(ref _read) >= Capacity)
            return false;

        _items[write & Mask] = tick;
        Volatile.Write(ref _write, write + 1);
        return true;
    }

    public bool TryDequeue(out WheelTick tick)
    {
        int read = _read;
        if (read == Volatile.Read(ref _write))
        {
            tick = default;
            return false;
        }

        tick = _items[read & Mask];
        Volatile.Write(ref _read, read + 1);
        return true;
    }
}
