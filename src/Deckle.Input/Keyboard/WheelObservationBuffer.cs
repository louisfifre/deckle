namespace Deckle.Input;

// Raw Input supplies device identity while the hook supplies otherwise
// invisible wheel messages. Both sources are held briefly so one physical
// transition publishes exactly once, preferring the Raw Input observation.
// The arrays are fixed: after construction the input path does not allocate.
internal sealed class WheelObservationBuffer
{
    internal const double RetentionMs = 40;
    private const int CapacityPerSource = 64;

    private readonly MouseWheelEvent[] _hook = new MouseWheelEvent[CapacityPerSource];
    private readonly MouseWheelEvent[] _raw = new MouseWheelEvent[CapacityPerSource];
    private int _hookCount;
    private int _rawCount;

    public bool HasPending => _hookCount != 0 || _rawCount != 0;

    // Returns one publishable transition when a pair completed or a full
    // source buffer had to release its oldest observation.
    public bool Observe(in MouseWheelEvent wheelEvent, out MouseWheelEvent publish)
    {
        MouseWheelEvent[] own;
        MouseWheelEvent[] opposite;
        ref int ownCount = ref _hookCount;
        ref int oppositeCount = ref _rawCount;

        if (wheelEvent.Source == WheelEventSource.RawInput)
        {
            own = _raw;
            opposite = _hook;
            ownCount = ref _rawCount;
            oppositeCount = ref _hookCount;
        }
        else
        {
            own = _hook;
            opposite = _raw;
        }

        int match = FindMatch(opposite, oppositeCount, in wheelEvent);
        if (match >= 0)
        {
            MouseWheelEvent paired = opposite[match];
            RemoveAt(opposite, ref oppositeCount, match);
            publish = wheelEvent.Source == WheelEventSource.RawInput
                ? wheelEvent
                : paired;
            return true;
        }

        if (ownCount == own.Length)
        {
            publish = own[0];
            RemoveAt(own, ref ownCount, 0);
            own[ownCount++] = wheelEvent;
            return true;
        }

        own[ownCount++] = wheelEvent;
        publish = default;
        return false;
    }

    public bool TryDequeueExpired(double nowMs, out MouseWheelEvent wheelEvent)
    {
        bool hookExpired = _hookCount > 0
            && nowMs - _hook[0].TimestampMs >= RetentionMs;
        bool rawExpired = _rawCount > 0
            && nowMs - _raw[0].TimestampMs >= RetentionMs;

        if (!hookExpired && !rawExpired)
        {
            wheelEvent = default;
            return false;
        }

        bool takeHook = hookExpired
            && (!rawExpired || _hook[0].TimestampMs <= _raw[0].TimestampMs);
        MouseWheelEvent[] source = takeHook ? _hook : _raw;
        ref int count = ref (takeHook ? ref _hookCount : ref _rawCount);
        wheelEvent = source[0];
        RemoveAt(source, ref count, 0);
        return true;
    }

    public bool TryDequeue(out MouseWheelEvent wheelEvent)
    {
        if (!HasPending)
        {
            wheelEvent = default;
            return false;
        }

        bool takeHook = _hookCount > 0
            && (_rawCount == 0 || _hook[0].TimestampMs <= _raw[0].TimestampMs);
        MouseWheelEvent[] source = takeHook ? _hook : _raw;
        ref int count = ref (takeHook ? ref _hookCount : ref _rawCount);
        wheelEvent = source[0];
        RemoveAt(source, ref count, 0);
        return true;
    }

    private static int FindMatch(
        MouseWheelEvent[] candidates,
        int count,
        in MouseWheelEvent wheelEvent)
    {
        for (int index = 0; index < count; index++)
        {
            MouseWheelEvent candidate = candidates[index];
            if (candidate.Axis == wheelEvent.Axis
                && candidate.Delta == wheelEvent.Delta
                && candidate.IsInjected == wheelEvent.IsInjected
                && Math.Abs(candidate.TimestampMs - wheelEvent.TimestampMs) <= RetentionMs)
            {
                return index;
            }
        }

        return -1;
    }

    private static void RemoveAt(
        MouseWheelEvent[] source,
        ref int count,
        int index)
    {
        for (int current = index; current < count - 1; current++)
            source[current] = source[current + 1];
        count--;
    }
}
