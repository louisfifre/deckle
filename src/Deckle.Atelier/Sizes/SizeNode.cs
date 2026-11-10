namespace Deckle.Atelier;

// One folder of a scan. A worker reads it once (its loose files and its
// sub-folders), then the node waits for its sub-folders; the last one to finish
// completes it, which adds its total to its parent. Totals therefore climb the
// tree bottom-up without any worker waiting on another.
//
// What is kept after the scan: a node at depth <= NavigableDepth keeps its
// sub-folders and its heaviest files, so the report can be re-rooted on it
// (SizeReport.ForFolder) without reading the disk again. Its sub-folders are
// kept as rows (a name and a total). Deeper nodes live only while the scan
// climbs through them, so memory follows the top of the tree, not the whole
// disk (SizeScanOptions.NavigableDepth says how much that costs).
internal sealed class SizeNode
{
    private long _total = -1;
    private long _childBytes;
    private long _filesBeneath;
    private long _foldersBeneath;
    private long _unreadableBeneath;
    private int _pending;

    private string? _path;

    public SizeNode(string name, string path, SizeNode? parent, bool keepsChildren, int heaviestCapacity)
    {
        Name = name;
        _path = path;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        KeepsChildren = keepsChildren;
        Heaviest = new HeaviestFiles(heaviestCapacity);
    }

    public string Name { get; }

    // The full path is held only while the folder waits to be read; after
    // that it is rebuilt from the parents on demand. A wide tree keeps one row
    // per folder (90 000 under C:\Windows\WinSxS), and its paths were most of
    // the memory a report held.
    public string Path => Volatile.Read(ref _path) ?? System.IO.Path.Join(Parent!.Path, Name);

    public void ForgetPath()
    {
        if (Parent is not null) Volatile.Write(ref _path, null);
    }
    public SizeNode? Parent { get; }
    public int Depth { get; }
    public bool KeepsChildren { get; }

    // Written by the one worker that reads this folder, before any sub-folder is
    // queued, and read only after that.
    public long LooseBytes { get; private set; }
    public int LooseFiles { get; private set; }
    public bool Unreadable { get; private set; }
    public IReadOnlyList<SizeNode> Children { get; private set; } = [];

    // Locked by whoever touches it: the reading worker, then each finishing
    // sub-folder merging its own into it. Null once merged into a parent that
    // does not keep it.
    public HeaviestFiles? Heaviest { get; private set; }

    public bool IsComplete => Volatile.Read(ref _total) >= 0;
    public long Total => Volatile.Read(ref _total);

    // While the scan runs: what is measured so far beneath this folder.
    public long MeasuredBytes => LooseBytes + Volatile.Read(ref _childBytes);

    public long FilesBeneath => Volatile.Read(ref _filesBeneath);
    public long FoldersBeneath => Volatile.Read(ref _foldersBeneath);
    public long UnreadableBeneath => Volatile.Read(ref _unreadableBeneath);

    public void Read(long looseBytes, int looseFiles, bool unreadable, List<SizeNode> children)
    {
        LooseBytes = looseBytes;
        LooseFiles = looseFiles;
        Unreadable = unreadable;
        _filesBeneath = looseFiles;
        _unreadableBeneath = unreadable ? 1 : 0;
        if (KeepsChildren) Children = children;
        // Itself plus each sub-folder: the node completes when all have
        // released it.
        Volatile.Write(ref _pending, children.Count + 1);
    }

    // Returns true for the release that completes the node.
    public bool Release() => Interlocked.Decrement(ref _pending) == 0;

    public void Complete()
    {
        Volatile.Write(ref _total, LooseBytes + Volatile.Read(ref _childBytes));
    }

    public void Absorb(SizeNode child)
    {
        Interlocked.Add(ref _childBytes, child.Total);
        Interlocked.Add(ref _filesBeneath, child.FilesBeneath);
        Interlocked.Add(ref _foldersBeneath, child.FoldersBeneath + 1);
        Interlocked.Add(ref _unreadableBeneath, child.UnreadableBeneath);

        HeaviestFiles? theirs = child.Heaviest;
        if (theirs is not null && Heaviest is { } mine)
        {
            lock (mine) lock (theirs) mine.OfferAll(theirs);
        }
        if (!child.KeepsChildren) child.Heaviest = null;
    }

    public IReadOnlyList<HeavyFile> HeaviestSoFar()
    {
        HeaviestFiles? heap = Heaviest;
        if (heap is null) return [];
        lock (heap) return heap.Descending();
    }
}
