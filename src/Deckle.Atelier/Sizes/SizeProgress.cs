namespace Deckle.Atelier;

public enum SizeScanState
{
    Running,
    Completed,
    Cancelled,
    // The place itself could not be read, or the scan stopped on an error it
    // did not expect; SizeReport.PlaceFailure or the log says which.
    Failed,
}

// Where a scan stands, read without a lock (12.23): what is measured so far,
// and how many of the place's sub-folders are measured, for the progress bar.
// ChildFolders is -1 until the place itself has been read.
//
// UnreadableFolders counts every folder beneath the place that could not be
// read, fully or in part; LinksSkipped counts the junctions, symbolic links and
// mount points not followed.
public readonly record struct SizeProgress(
    SizeScanState State,
    long FoldersSeen,
    long FilesSeen,
    long BytesSummed,
    long UnreadableFolders,
    long LinksSkipped,
    long ChildFoldersMeasured,
    long ChildFolders,
    TimeSpan Elapsed);
