namespace Deckle.Audio.Internal;

// Recording-local accumulator of the take's 16 kHz mono float samples.
// Capacity grows with the captured duration, so a short take never reserves a
// large-object-heap block.
//
// With retain false it keeps nothing: each Append overwrites the same first
// samples, so a take that only feeds a live meter holds one packet's worth of
// memory however long it runs. Count still totals every sample appended, which
// is what the duration cap and the take's length read.
internal sealed class FloatTakeBuffer(int initialCapacity, bool retain = true)
{
    private float[] _samples = new float[initialCapacity];
    private int _stored;

    // Total samples appended over the take, retained or not.
    public long Count { get; private set; }

    public void Append(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;

        int offset = retain ? _stored : 0;
        EnsureCapacity(checked(offset + samples.Length));
        samples.CopyTo(_samples.AsSpan(offset));
        Count += samples.Length;
        if (retain) _stored += samples.Length;
    }

    // Empty when the buffer does not retain.
    public float[] ToArray() => _samples.AsSpan(0, _stored).ToArray();

    private void EnsureCapacity(int required)
    {
        if (required <= _samples.Length) return;
        int capacity = Math.Max(required, checked(_samples.Length * 2));
        Array.Resize(ref _samples, capacity);
    }
}
