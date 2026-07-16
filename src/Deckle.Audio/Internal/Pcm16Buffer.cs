using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// Recording-local PCM16 accumulator. Capacity follows captured duration
// instead of reserving a minute on the large-object heap at every hotkey.
internal sealed class Pcm16Buffer(int initialCapacity)
{
    private byte[] _bytes = new byte[initialCapacity];

    public int Count { get; private set; }

    public ReadOnlySpan<byte> Append(nint source, int count)
    {
        if (count <= 0) return ReadOnlySpan<byte>.Empty;

        EnsureCapacity(checked(Count + count));
        int offset = Count;
        Marshal.Copy(source, _bytes, offset, count);
        Count += count;
        return _bytes.AsSpan(offset, count);
    }

    public byte[] ToArray() => _bytes.AsSpan(0, Count).ToArray();

    private void EnsureCapacity(int required)
    {
        if (required <= _bytes.Length) return;
        int capacity = Math.Max(required, checked(_bytes.Length * 2));
        Array.Resize(ref _bytes, capacity);
    }
}
