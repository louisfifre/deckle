using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

internal sealed class PcmBuffer(int initialCapacity)
{
    private float[] _samples = new float[initialCapacity];

    public int Count { get; private set; }
    public ReadOnlyMemory<float> WrittenMemory => _samples.AsMemory(0, Count);

    public void Append(nint source, int count)
    {
        if (count <= 0) return;
        EnsureCapacity(checked(Count + count));
        Marshal.Copy(source, _samples, Count, count);
        Count += count;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _samples.Length) return;
        int capacity = Math.Max(required, checked(_samples.Length * 2));
        Array.Resize(ref _samples, capacity);
    }
}
