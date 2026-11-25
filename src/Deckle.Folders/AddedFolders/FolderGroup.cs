namespace Deckle.Folders;

// A named set of added folders. The pane shows it as its name and a chevron
// with its folders under it, and Home as a section of its own; a folder is in
// at most one group, or in none.
//
// The id is opaque and stable (a new one is a Guid in "N" form) and is what a
// folder's GroupId points at, so renaming a group changes nothing that refers
// to it. The name is the person's own word and is never an identity: two
// groups cannot share one (AddedFolderStore.IsGroupNameTaken), but the id is
// what tells them apart.
public sealed record FolderGroup(string Id, string Name);
