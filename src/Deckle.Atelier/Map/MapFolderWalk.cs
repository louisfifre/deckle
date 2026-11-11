namespace Deckle.Atelier;

internal enum MapWalkFailure
{
    None,
    NotFound,
    AccessDenied,
}

internal sealed record MapWalkResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<MapSkip> Skipped,
    bool Truncated,
    MapWalkFailure Failure);

// Goes through a folder and its sub-folders, leaving out the folders that
// MapConventions names and every junction or symbolic link, and stops at
// MapLimits.MaxEntries. Hidden items are walked: .github and .claude are
// hidden on some machines and hold what the map looks for.
internal static class MapFolderWalk
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static MapWalkResult Walk(string root, MapLimits limits, CancellationToken cancellation)
    {
        var files = new List<string>();
        var skipped = new List<MapSkip>();
        if (!Directory.Exists(root)) return new([], [], false, MapWalkFailure.NotFound);

        var pending = new Stack<string>();
        pending.Push(root);
        int entries = 0;
        bool first = true;
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            List<FileSystemInfo> children;
            try
            {
                children = [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options)];
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                if (first) return new([], [], false, ex is UnauthorizedAccessException ? MapWalkFailure.AccessDenied : MapWalkFailure.NotFound);
                skipped.Add(new MapSkip(MapPaths.Relative(root, folder), MapSkipReason.Unreadable));
                continue;
            }
            first = false;

            foreach (FileSystemInfo child in children)
            {
                if (++entries > limits.MaxEntries)
                {
                    skipped.Add(new MapSkip(MapPaths.Relative(root, folder), MapSkipReason.EntryLimit));
                    return new(files, skipped, true, MapWalkFailure.None);
                }
                if (child is FileInfo)
                {
                    files.Add(child.FullName);
                    continue;
                }
                if (MapConventions.SkippedFolders.TryGetValue(child.Name, out MapSkipReason reason))
                {
                    skipped.Add(new MapSkip(MapPaths.Relative(root, child.FullName), reason));
                }
                else if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    skipped.Add(new MapSkip(MapPaths.Relative(root, child.FullName), MapSkipReason.LinkedFolder));
                }
                else
                {
                    pending.Push(child.FullName);
                }
            }
        }
        return new(files, skipped, false, MapWalkFailure.None);
    }
}

// Paths inside the map: relative to the folder looked at, "/" between segments.
internal static class MapPaths
{
    public static string Relative(string root, string fullPath)
    {
        string relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return relative == "." ? "" : relative;
    }

    // A path read in a document, resolved against the document's folder (or the
    // root for a leading "/"). Null when it leaves the folder or cannot be a path.
    public static string? Resolve(string root, string documentFolder, string raw)
    {
        string target = raw.Replace('\\', '/');
        if (target.Length == 0 || target.IndexOfAny(['<', '>', '"', '|', '*', '?']) >= 0) return null;
        string basis = target.StartsWith('/') ? root : Path.Combine(root, documentFolder);
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(basis, target.TrimStart('/')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (relative == "." || relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || Path.IsPathRooted(relative)) return null;
        return relative.TrimEnd('/');
    }
}
