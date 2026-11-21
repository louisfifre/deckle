using Deckle.Core;

namespace Deckle.Projects;

// What Deckle keeps of one project (spec, Behaviours): its folder, the person's
// name for it, its category and, through its place in the store's list, its
// place in that category, the actions pinned on its page and which one is main.
// Everything else on screen is read from the folder each time.
//
// Name is the person's label in Deckle only; null means "the folder's own
// name". The path never changes when the name does. Main, when set, is always
// one of Pinned: the store refuses anything else.
public sealed record ProjectEntry(
    string Id,
    string Path,
    string? Name,
    string? CategoryId,
    IReadOnlyList<PinnedAction> Pinned,
    ActionRef? Main,
    DateTimeOffset AddedAt)
{
    public string DisplayName => Name ?? FolderPaths.DisplayName(Path);

    // The pinned action marked main, with the label last seen; null when the
    // project has no main action.
    public PinnedAction? MainAction => Main is null ? null : Pinned.FirstOrDefault(p => p.Action == Main);

    public bool IsPinned(ActionRef action) => Pinned.Any(p => p.Action == action);
}
