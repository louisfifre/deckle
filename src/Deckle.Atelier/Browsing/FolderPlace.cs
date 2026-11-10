namespace Deckle.Atelier;

// Where the window stands: a folder inside one added folder. The address bar
// starts at the added folder, not at the drive (boards 12.1, 12.12a: "Photos ›
// 2026 › …"), and Up stops there (Up is disabled at an added folder's root,
// 12.1), because the app shows nothing outside what was added.
public sealed record FolderPlace
{
    public AddedFolder Root { get; }
    public string Path { get; }

    public FolderPlace(AddedFolder root, string path)
    {
        if (!FolderPaths.IsInside(root.Path, path))
            throw new ArgumentException("The place must sit inside its added folder.", nameof(path));
        Root = root;
        Path = FolderPaths.Normalize(path);
    }

    public static FolderPlace AtRoot(AddedFolder root) => new(root, root.Path);

    public bool IsRoot => FolderPaths.Comparer.Equals(Path, Root.Path);

    public string Name => IsRoot ? Root.Name : System.IO.Path.GetFileName(Path);

    // Null at the added folder's root: Up has nowhere to go.
    public FolderPlace? Parent =>
        IsRoot ? null : new FolderPlace(Root, System.IO.Path.GetDirectoryName(Path)!);

    public FolderPlace Enter(string childPath) => new(Root, childPath);

    // The added folder first, then one segment per folder down to this one.
    public IReadOnlyList<PathSegment> Segments
    {
        get
        {
            var segments = new List<PathSegment> { new(Root.Name, Root.Path) };
            if (IsRoot) return segments;
            string relative = System.IO.Path.GetRelativePath(Root.Path, Path);
            string current = Root.Path;
            foreach (string part in relative.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, part);
                segments.Add(new PathSegment(part, current));
            }
            return segments;
        }
    }

    public bool Equals(FolderPlace? other) =>
        other is not null
        && FolderPaths.Comparer.Equals(Root.Path, other.Root.Path)
        && FolderPaths.Comparer.Equals(Path, other.Path);

    public override int GetHashCode() =>
        HashCode.Combine(FolderPaths.Comparer.GetHashCode(Root.Path), FolderPaths.Comparer.GetHashCode(Path));
}

// One segment of the address bar: what it shows and where a click on it goes.
public sealed record PathSegment(string Name, string Path);
