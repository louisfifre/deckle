using System.Diagnostics;
using System.Globalization;

namespace Deckle.Atelier;

// One row of a folder: a file or a sub-folder. A folder has no size here, as
// in File Explorer's details view (12.1 leaves the size cell empty); the size
// of a folder is the Sizes tool's job.
public sealed record FolderEntry(string Name, string Path, bool IsFolder, long? Size, DateTime Modified);

public enum FolderListingFailure
{
    None,
    NotFound,
    AccessDenied,
    Unreadable,
}

public sealed record FolderListingResult(IReadOnlyList<FolderEntry> Entries, FolderListingFailure Failure)
{
    public bool Succeeded => Failure == FolderListingFailure.None;

    public static FolderListingResult Failed(FolderListingFailure failure) => new([], failure);
}

// Reads one folder, one level deep. Hidden and system items are left out, as
// File Explorer does by default. Folders come first, then files, each sorted
// by name the way Explorer sorts: case ignored and numbers in order ("2" before
// "10"). The listing touches the disk, so callers run it off the UI thread.
public static class FolderListing
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    // Explorer's order: CompareOptions.NumericOrdering (.NET 10) compares runs
    // of digits as numbers, like StrCmpLogicalW.
    public static readonly IComparer<string> NameOrder = Comparer<string>.Create((a, b) =>
        CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

    public static FolderListingResult List(string folder)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return Fail(FolderListingFailure.NotFound);

            var entries = new List<FolderEntry>();
            foreach (FileSystemInfo info in dir.EnumerateFileSystemInfos("*", Options))
            {
                entries.Add(info is FileInfo file
                    ? new FolderEntry(file.Name, file.FullName, false, file.Length, file.LastWriteTime)
                    : new FolderEntry(info.Name, info.FullName, true, null, info.LastWriteTime));
            }
            entries.Sort(Order);
            DeckleAtelierSource.Log.FolderListed(entries.Count, clock.ElapsedMilliseconds);
            return new FolderListingResult(entries, FolderListingFailure.None);
        }
        catch (DirectoryNotFoundException) { return Fail(FolderListingFailure.NotFound); }
        catch (UnauthorizedAccessException) { return Fail(FolderListingFailure.AccessDenied); }
        catch (IOException) { return Fail(FolderListingFailure.Unreadable); }
    }

    // The sub-folders alone, for a chevron of the address bar (12.12a) and the
    // address suggestions (12.12b).
    public static IReadOnlyList<FolderEntry> Subfolders(string folder)
    {
        FolderListingResult listing = List(folder);
        return [.. listing.Entries.Where(e => e.IsFolder)];
    }

    private static int Order(FolderEntry a, FolderEntry b)
    {
        if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
        return NameOrder.Compare(a.Name, b.Name);
    }

    private static FolderListingResult Fail(FolderListingFailure failure)
    {
        DeckleAtelierSource.Log.FolderListingFailed(failure.ToString());
        return FolderListingResult.Failed(failure);
    }
}
