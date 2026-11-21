namespace Deckle.Folders;

// Whether an added folder can be opened: the folder may have been moved,
// renamed or deleted since it was added, or sit on a drive that is not
// plugged in, or refuse to be read.
public enum AddedFolderReachability
{
    Reachable,
    NotFound,
    Unreadable,
}

// Asks the disk whether an added folder is still there and readable. It reads
// one entry at most, so it stays quick on a large folder; a folder on a drive
// that does not answer can still take the drive's time, so callers run it off
// the UI thread.
public static class AddedFolderReach
{
    public static AddedFolderReachability Check(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return AddedFolderReachability.NotFound;
            // Exists says nothing of the rights: listing one entry does.
            using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return AddedFolderReachability.Reachable;
        }
        catch (DirectoryNotFoundException) { return AddedFolderReachability.NotFound; }
        catch (UnauthorizedAccessException) { return AddedFolderReachability.Unreadable; }
        catch (IOException) { return AddedFolderReachability.Unreadable; }
    }
}
