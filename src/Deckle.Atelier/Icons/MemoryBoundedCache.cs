namespace Deckle.Atelier;

// Pictures kept in memory up to a budget in bytes. When an addition passes
// the budget, the pictures used least recently leave first, so the ones a
// person is scrolling around stay. A value may be null: "this key has no
// picture" is remembered too, so a type the shell cannot draw is asked once.
//
// Not thread-safe: the icon provider touches it from the UI thread only,
// which serialises every call without a lock.
public sealed class MemoryBoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly long _budgetBytes;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _byUse = []; // most recent first

    public MemoryBoundedCache(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        _budgetBytes = budgetBytes;
    }

    public long UsedBytes { get; private set; }
    public int Count => _entries.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            _byUse.Remove(node);
            _byUse.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    // A picture larger than the whole budget is not kept: keeping it would
    // empty the cache for one item.
    public void Add(TKey key, TValue value, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Remove(key);
        if (bytes > _budgetBytes) return;

        _entries[key] = _byUse.AddFirst(new Entry(key, value, bytes));
        UsedBytes += bytes;
        while (UsedBytes > _budgetBytes && _byUse.Last is { } oldest) Remove(oldest.Value.Key);
    }

    private void Remove(TKey key)
    {
        if (!_entries.Remove(key, out LinkedListNode<Entry>? node)) return;
        _byUse.Remove(node);
        UsedBytes -= node.Value.Bytes;
    }

    private sealed record Entry(TKey Key, TValue Value, long Bytes);
}
