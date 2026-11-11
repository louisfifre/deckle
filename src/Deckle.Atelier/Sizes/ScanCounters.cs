namespace Deckle.Atelier;

// The running totals of one scan, written by every worker and read by the view
// at its own pace. Each counter is one interlocked add; reading them takes no
// lock, so a view may poll as often as it draws.
internal sealed class ScanCounters
{
    private long _files;
    private long _folders;
    private long _bytes;
    private long _unreadable;
    private long _links;
    private long _childrenMeasured;
    private long _children = -1;

    public void FileSeen(long size)
    {
        Interlocked.Increment(ref _files);
        Interlocked.Add(ref _bytes, size);
    }

    public void FolderRead(bool unreadable)
    {
        Interlocked.Increment(ref _folders);
        if (unreadable) Interlocked.Increment(ref _unreadable);
    }

    public void LinkSkipped() => Interlocked.Increment(ref _links);

    public void ChildrenKnown(int count) => Volatile.Write(ref _children, count);

    public void ChildMeasured() => Interlocked.Increment(ref _childrenMeasured);

    public SizeProgress Snapshot(SizeScanState state, TimeSpan elapsed) => new(
        state,
        Volatile.Read(ref _folders),
        Volatile.Read(ref _files),
        Volatile.Read(ref _bytes),
        Volatile.Read(ref _unreadable),
        Volatile.Read(ref _links),
        Volatile.Read(ref _childrenMeasured),
        Volatile.Read(ref _children),
        elapsed);
}
