namespace Deckle.Atelier;

// One sub-folder's row in the Sizes list (12.7). Bytes is null while the
// folder is not measured yet: the row shows "…" in place of its size (12.23).
// Share is the part of the place's total that sets the bar's length, between
// 0 and 1; while the scan runs it is the part of what is measured so far.
// UnreadableFolders counts the folders beneath this row, itself included, that
// could not be read: its size leaves them out.
public sealed record FolderSizeRow(
    string Name,
    string Path,
    long? Bytes,
    double Share,
    long UnreadableFolders);

// What the Sizes tool shows for a place: the place's total, its own loose files
// (the first row of the list), one row per sub-folder, and the heaviest files
// beneath it. A report taken while a scan runs is partial and says so through
// State; a report of a cancelled scan keeps what was measured.
public sealed class SizeReport
{
    private readonly SizeNode? _root;

    internal SizeReport(string place, SizeNode? root, SizeScanState state,
        FolderListingFailure placeFailure, DateTimeOffset? finishedAt, TimeSpan elapsed)
    {
        _root = root;
        Place = place;
        State = state;
        PlaceFailure = placeFailure;
        FinishedAt = finishedAt;
        Elapsed = elapsed;

        if (root is null)
        {
            Folders = [];
            Heaviest = [];
            return;
        }

        bool complete = root.IsComplete;
        TotalBytes = complete ? root.Total : root.MeasuredBytes;
        LooseFilesBytes = root.LooseBytes;
        LooseFileCount = root.LooseFiles;
        LooseFilesShare = Share(root.LooseBytes, TotalBytes);
        UnreadableFolders = root.UnreadableBeneath;
        PlaceUnreadable = root.Unreadable;
        Heaviest = root.HeaviestSoFar();

        var rows = new List<FolderSizeRow>(root.Children.Count);
        foreach (SizeNode child in root.Children)
        {
            long? bytes = child.IsComplete ? child.Total : null;
            rows.Add(new FolderSizeRow(child.Name, child.Path, bytes,
                bytes is long b ? Share(b, TotalBytes) : 0, child.UnreadableBeneath));
        }
        rows.Sort(ByBytesThenName);
        Folders = rows;
    }

    public string Place { get; }
    public SizeScanState State { get; }
    public bool IsComplete => State == SizeScanState.Completed;

    // Why the place itself gave nothing: gone, refused, or unreadable.
    public FolderListingFailure PlaceFailure { get; }

    // The place could be opened but not listed to the end: what it shows is
    // what was read before the error.
    public bool PlaceUnreadable { get; }

    public long TotalBytes { get; }
    public long LooseFilesBytes { get; }
    public int LooseFileCount { get; }
    public double LooseFilesShare { get; }
    public IReadOnlyList<FolderSizeRow> Folders { get; }
    public IReadOnlyList<HeavyFile> Heaviest { get; }

    // Folders beneath the place, the place included, that could not be read:
    // the sizes leave them out, and the view says how many.
    public long UnreadableFolders { get; }

    // When the scan that measured this ended, for "Analyser à nouveau (F5)"'s
    // tooltip (12.21); null while it runs.
    public DateTimeOffset? FinishedAt { get; }
    public TimeSpan Elapsed { get; }

    // The report of a sub-folder this scan already measured, without reading the
    // disk again; null when the folder lies deeper than the scan kept
    // (SizeScanOptions.NavigableDepth), is not beneath the place, or is not
    // measured yet. A folder fully measured by a scan that was then cancelled
    // still gives a complete report.
    public SizeReport? ForFolder(string folder)
    {
        if (_root is null) return null;
        string target = FolderPaths.Normalize(folder);
        if (!FolderPaths.IsInside(_root.Path, target)) return null;
        string relative = System.IO.Path.GetRelativePath(_root.Path, target);
        if (relative == ".") return this;

        SizeNode node = _root;
        foreach (string part in relative.Split(System.IO.Path.DirectorySeparatorChar))
        {
            SizeNode? next = null;
            foreach (SizeNode child in node.Children)
            {
                if (string.Equals(child.Name, part, StringComparison.OrdinalIgnoreCase)) { next = child; break; }
            }
            if (next is null) return null;
            node = next;
        }
        if (!node.KeepsChildren || !node.IsComplete) return null;
        return new SizeReport(node.Path, node, SizeScanState.Completed, FolderListingFailure.None, FinishedAt, Elapsed);
    }

    private static double Share(long part, long whole) => whole <= 0 ? 0 : (double)part / whole;

    // Heaviest first, as FolderSize lists; a folder not measured yet goes last,
    // and equal sizes fall back on Explorer's name order.
    private static int ByBytesThenName(FolderSizeRow a, FolderSizeRow b)
    {
        if (a.Bytes != b.Bytes)
        {
            if (a.Bytes is null) return 1;
            if (b.Bytes is null) return -1;
            return b.Bytes.Value.CompareTo(a.Bytes.Value);
        }
        return FolderListing.NameOrder.Compare(a.Name, b.Name);
    }
}
