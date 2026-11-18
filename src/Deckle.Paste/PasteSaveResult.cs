namespace Deckle.Paste;

// How a paste ended. SavedPaths lists every file or folder written, in order,
// also when a copy of several files stopped part way. Error is one of the
// PasteSaveErrors sentences when Succeeded is false, null otherwise.
public sealed record PasteSaveResult(bool Succeeded, IReadOnlyList<string> SavedPaths, string? Error)
{
    internal static PasteSaveResult Saved(IReadOnlyList<string> paths) => new(true, paths, null);

    internal static PasteSaveResult Failed(string error, IReadOnlyList<string>? savedSoFar = null) =>
        new(false, savedSoFar ?? [], error);
}
