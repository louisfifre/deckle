namespace Deckle.Atelier;

public enum AddressFailure
{
    None,
    Empty,
    NotFound,
    OutsideAddedFolders,
}

public sealed record AddressResolution(FolderPlace? Place, AddressFailure Failure);

// The text mode of the address bar (12.12b): what a typed path leads to, and
// the folders that complete what is being typed. A typed path is accepted only
// inside an added folder, since the app shows nothing else.
public static class AddressResolver
{
    public const int MaxSuggestions = 8;

    public static AddressResolution Resolve(string text, AddedFolderStore folders)
    {
        string trimmed = text.Trim().Trim('"');
        if (trimmed.Length == 0) return new(null, AddressFailure.Empty);

        string path;
        try { path = FolderPaths.Normalize(Environment.ExpandEnvironmentVariables(trimmed)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(null, AddressFailure.NotFound);
        }

        AddedFolder? root = folders.Owning(path);
        if (root is null) return new(null, AddressFailure.OutsideAddedFolders);
        if (!Directory.Exists(path)) return new(null, AddressFailure.NotFound);
        return new(new FolderPlace(root, path), AddressFailure.None);
    }

    // Full paths of the folders whose path starts with the text: the
    // sub-folders of the typed parent whose name starts with the typed last
    // part, or, while the text is still above every added folder, the added
    // folders it leads to.
    public static IReadOnlyList<string> Suggestions(string text, AddedFolderStore folders)
    {
        string typed = text.Trim().Trim('"');
        if (typed.Length == 0) return [];

        var rootsAhead = folders.Folders
            .Where(f => f.Path.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
                        && !FolderPaths.Comparer.Equals(f.Path, typed.TrimEnd('\\')))
            .Select(f => f.Path);

        string? parent;
        string prefix;
        if (typed.EndsWith('\\'))
        {
            parent = typed;
            prefix = "";
        }
        else
        {
            try { parent = System.IO.Path.GetDirectoryName(typed); }
            catch (ArgumentException) { return [.. rootsAhead.Take(MaxSuggestions)]; }
            prefix = System.IO.Path.GetFileName(typed);
        }

        IEnumerable<string> below = [];
        if (!string.IsNullOrEmpty(parent) && folders.Owning(parent) is not null)
        {
            below = FolderListing.Subfolders(parent)
                .Where(e => e.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                .Select(e => e.Path);
        }

        return [.. rootsAhead.Concat(below).Distinct(FolderPaths.Comparer).Take(MaxSuggestions)];
    }
}
