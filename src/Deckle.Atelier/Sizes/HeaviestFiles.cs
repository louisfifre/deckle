namespace Deckle.Atelier;

// The N heaviest files seen so far, in a min-heap of N slots. The root is the
// lightest file kept, so a new file is compared with that one value and almost
// every file of a large tree leaves at once: the cost per file is one
// comparison, and log N moves only for the few that enter. Accepts lets the
// caller skip building a path for a file that would not enter.
//
// Not thread-safe: the folder that owns it locks around every call.
internal sealed class HeaviestFiles
{
    private readonly HeavyFile[] _heap;
    private int _count;

    public HeaviestFiles(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _heap = new HeavyFile[capacity];
    }

    public int Count => _count;

    // An equal size does not enter once the heap is full: the file seen first
    // stays, so two runs on the same tree keep the same files.
    public bool Accepts(long size)
        => _heap.Length > 0 && (_count < _heap.Length || size > _heap[0].Size);

    public void Offer(HeavyFile file)
    {
        if (!Accepts(file.Size)) return;
        if (_count < _heap.Length)
        {
            _heap[_count] = file;
            SiftUp(_count++);
        }
        else
        {
            _heap[0] = file;
            SiftDown(0);
        }
    }

    public void OfferAll(HeaviestFiles other)
    {
        for (int i = 0; i < other._count; i++) Offer(other._heap[i]);
    }

    // Heaviest first; equal sizes by path, so the order does not depend on which
    // worker finished first.
    public IReadOnlyList<HeavyFile> Descending()
    {
        var files = new HeavyFile[_count];
        Array.Copy(_heap, files, _count);
        Array.Sort(files, static (a, b) =>
        {
            int bySize = b.Size.CompareTo(a.Size);
            return bySize != 0 ? bySize : string.CompareOrdinal(a.Path, b.Path);
        });
        return files;
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (_heap[parent].Size <= _heap[i].Size) return;
            (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
            i = parent;
        }
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, lightest = i;
            if (left < _count && _heap[left].Size < _heap[lightest].Size) lightest = left;
            if (right < _count && _heap[right].Size < _heap[lightest].Size) lightest = right;
            if (lightest == i) return;
            (_heap[lightest], _heap[i]) = (_heap[i], _heap[lightest]);
            i = lightest;
        }
    }
}
