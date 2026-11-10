using System.IO.Enumeration;

namespace Deckle.Atelier;

// Reads one folder for the sizes: sums its loose files, offers each to the
// folder's heaviest, and yields its sub-folders. One pass over the directory
// entries, as NtQueryDirectoryFile returns them: no file is opened, so nothing
// is read from a file's data.
//
// Why no file is opened. A OneDrive file that is only online is a placeholder
// marked FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS; reading its data, not listing
// it, fetches it from the cloud. The size of the entry is its logical size,
// known without the data. (learn.microsoft.com, "File Attribute Constants" and
// "Handling placeholders".)
//
// Why a link is not followed, and how a link is told apart. A junction, a
// directory symbolic link or a volume mount point leads to a tree counted
// elsewhere, or to a loop; following it would count bytes twice. All three carry
// FILE_ATTRIBUTE_REPARSE_POINT, but so does a OneDrive folder: placeholders are
// "exposed" to most processes, reparse attribute included
// (RtlSetProcessPlaceholderCompatibilityMode). Skipping every reparse point
// would leave OneDrive out, so a reparse folder is asked for its link target,
// which .NET reads only for the symbolic-link and mount-point tags and leaves
// null for the others.
//
// The enumerator does its accounting in ShouldIncludeEntry and lets only
// sub-folders through: a file costs no allocation unless it enters the heaviest.
internal sealed class FolderReader : FileSystemEnumerator<string>
{
    private readonly SizeNode _node;
    private readonly ScanCounters _counters;
    private readonly CancellationToken _cancel;
    private int _sinceCheck;

    private FolderReader(SizeNode node, EnumerationOptions options, ScanCounters counters, CancellationToken cancel)
        : base(node.Path, options)
    {
        _node = node;
        _counters = counters;
        _cancel = cancel;
    }

    public long LooseBytes { get; private set; }
    public int LooseFiles { get; private set; }

    public static EnumerationOptions OptionsFor(int bufferSize) => new()
    {
        RecurseSubdirectories = false,
        // A size counts everything the disk holds, hidden and system files
        // included, as File Explorer's folder Properties does. The file tool
        // hides them; the sizes would not add up if they did too.
        AttributesToSkip = 0,
        // An unreadable folder must be counted, never skipped in silence: let
        // the error come out and the caller records it.
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        BufferSize = bufferSize,
    };

    // Reads the folder. Returns false when it could not be read, fully or in
    // part: what was summed before the error is kept.
    public static bool Read(SizeNode node, EnumerationOptions options, ScanCounters counters,
        CancellationToken cancel, List<string> subfolders, out long looseBytes, out int looseFiles)
    {
        FolderReader? reader = null;
        try
        {
            reader = new FolderReader(node, options, counters, cancel);
            while (reader.MoveNext()) subfolders.Add(reader.Current);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        finally
        {
            looseBytes = reader?.LooseBytes ?? 0;
            looseFiles = reader?.LooseFiles ?? 0;
            reader?.Dispose();
        }
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (++_sinceCheck == 512)
        {
            _sinceCheck = 0;
            _cancel.ThrowIfCancellationRequested();
        }

        if (entry.IsDirectory)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 && IsLink(entry.ToFullPath()))
            {
                _counters.LinkSkipped();
                return false;
            }
            return true;
        }

        long size = entry.Length;
        LooseBytes += size;
        LooseFiles++;
        _counters.FileSeen(size);

        HeaviestFiles heap = _node.Heaviest!;
        if (heap.Accepts(size))
        {
            var file = new HeavyFile(entry.FileName.ToString(), entry.ToFullPath(), size);
            lock (heap) heap.Offer(file);
        }
        return false;
    }

    protected override string TransformEntry(ref FileSystemEntry entry) => entry.FileName.ToString();

    // A reparse folder whose target cannot be read is treated as a link: not
    // following a folder costs a missing size, following a loop costs a wrong one.
    private static bool IsLink(string path)
    {
        try { return new DirectoryInfo(path).LinkTarget is not null; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
