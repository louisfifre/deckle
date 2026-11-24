namespace Deckle.Folders;

// Where the window can stand: Home, or a folder. The history holds both so
// that Back and Forward cross them as File Explorer's do: open a folder from
// Home, and Back returns to Home. Home carries nothing, so any two of it are
// the same destination.
public abstract record Destination;

public sealed record HomeDestination : Destination
{
    public static HomeDestination Instance { get; } = new();
}

public sealed record FolderDestination(FolderPlace Place) : Destination;
