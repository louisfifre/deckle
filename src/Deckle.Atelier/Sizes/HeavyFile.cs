namespace Deckle.Atelier;

// One of the heaviest files beneath a place: what the Sizes panel lists under
// "Les plus lourds en dessous" (12.7). Size is the logical size, the one File
// Explorer shows in its Size column.
public sealed record HeavyFile(string Name, string Path, long Size)
{
    // The folder that holds the file, written from the place: "" when the file
    // lies in the place itself, else "Sub\Deeper". The panel shows it under the
    // name (12.7) instead of repeating the whole path.
    public string FolderRelativeTo(string place)
    {
        string folder = System.IO.Path.GetDirectoryName(Path) ?? "";
        string relative = System.IO.Path.GetRelativePath(place, folder);
        return relative == "." ? "" : relative;
    }
}
