namespace Deckle.Atelier;

// One photo of a folder, as the gallery lists it before anything is read
// inside the file.
public sealed record PhotoFile(string Name, string Path, long Size, DateTime Modified)
{
    public bool IsRaw => PhotoFormats.IsRaw(Path);
}

public sealed record PhotoFolderResult(IReadOnlyList<PhotoFile> Photos, FolderListingFailure Failure)
{
    public bool Succeeded => Failure == FolderListingFailure.None;
}

// The photos of the folder the person is in (spec, Open question 2, drawn A):
// that folder alone, one level deep, in the file tool's order. It stands on
// FolderListing so hidden files, unreadable folders and the name order behave
// the same in both tools.
public static class PhotoFolder
{
    public static PhotoFolderResult List(string folder)
    {
        FolderListingResult listing = FolderListing.List(folder);
        if (!listing.Succeeded) return new PhotoFolderResult([], listing.Failure);

        List<PhotoFile> photos = [.. listing.Entries
            .Where(e => !e.IsFolder && PhotoFormats.IsPhoto(e.Name))
            .Select(e => new PhotoFile(e.Name, e.Path, e.Size ?? 0, e.Modified))];
        return new PhotoFolderResult(photos, FolderListingFailure.None);
    }
}
