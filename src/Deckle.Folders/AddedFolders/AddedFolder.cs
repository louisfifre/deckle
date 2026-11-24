using Deckle.Core;

namespace Deckle.Folders;

// One folder the person added. Deckle Folders shows nothing else of the disk
// (spec: "The app shows only the folders Louis added"), so every place the
// window can reach sits inside one of these.
//
// The path is stored normalized (FolderPaths.Normalize) so two spellings of
// the same folder are one folder. The name is what the pane, the address bar
// and the rail's tooltip show: the folder's own name, or "D:" for a drive.
// The icon is what the pane and the address bar draw for it (spec, Window):
// picked by FolderIconRule when the folder is added, changed by the person.
// The tool is the id of the view the folder was left in (IFoldersTool.Id,
// "photos"); null means the default tool. Each added folder remembers its own.
public sealed record AddedFolder(string Path, DateTimeOffset AddedAt, IconChoice Icon = default, string? Tool = null)
{
    public string Name => FolderPaths.DisplayName(Path);
}
