using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// Recording-local PCM16 accumulator. Capacity grows with the captured
// duration, so a short take never reserves a large-object-heap block.
//
// With retain false it keeps nothing: each Append overwrites the same
// first bytes, so a take that only feeds a live meter holds one buffer's
// worth of memory however long it runs. Count still totals every byte
// appended, which is what the duration cap and the take's length read.
internal sealed class Pcm16Buffer(int initialCapacity, bool retain = true)
{
    private byte[] _bytes = new byte[initialCapacity];
    private int _stored;

    // Total bytes appended over the take, retained or not.
    public long Count { get; private set; }

    public ReadOnlySpan<byte> Append(nint source, int count)
    {
        if (count <= 0) return ReadOnlySpan<byte>.Empty;

        int offset = retain ? _stored : 0;
        EnsureCapacity(checked(offset + count));
        Marshal.Copy(source, _bytes, offset, count);
        Count += count;
        if (retain) _stored += count;
        return _bytes.AsSpan(offset, count);
    }

    // Empty when the buffer does not retain.
    public byte[] ToArray() => _bytes.AsSpan(0, _stored).ToArray();

    private void EnsureCapacity(int required)
    {
        if (required <= _bytes.Length) return;
        int capacity = Math.Max(required, checked(_bytes.Length * 2));
        Array.Resize(ref _bytes, capacity);
    }
}
