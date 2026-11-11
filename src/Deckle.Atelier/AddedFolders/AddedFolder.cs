namespace Deckle.Atelier;

// One folder the person added. The Atelier shows nothing else of the disk
// (spec: "The app shows only the folders Louis added"), so every place the
// window can reach sits inside one of these.
//
// The path is stored normalized (FolderPaths.Normalize) so two spellings of
// the same folder are one folder. The name is what the pane, the address bar
// and the rail's tooltip show: the folder's own name, or "D:" for a drive.
public sealed record AddedFolder(string Path, DateTimeOffset AddedAt)
{
    public string Name => FolderPaths.DisplayName(Path);
}
