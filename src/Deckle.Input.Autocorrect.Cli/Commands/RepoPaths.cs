using System.IO;

namespace Deckle.Input.Autocorrect.Cli;

// Resolves the repo-relative locations the data pipeline reads and writes.
// The host runs from artifacts\bin\..., so we walk up from the running
// binary until the directory holding Deckle.Tests.sln — the repo root —
// is found. The raw inputs and the derived Data/ artifacts hang off it.
internal static class RepoPaths
{
    private const string RootMarker = "Deckle.Tests.sln";

    // Repo root: the first ancestor of the running binary that holds the
    // solution file. Throws when not found — every command needs it.
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RootMarker)))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Repo root not found: no ancestor of '{AppContext.BaseDirectory}' holds '{RootMarker}'.");
    }

    // Raw sources fetched by the data script (Lexique, count_1w, wiki corpora).
    public static string DefaultRawDir(string root) =>
        Path.Combine(root, "artifacts", "autocorrect-data", "raw");

    // Derived, shipped artifacts (lexicons, pair model) under the library.
    public static string DefaultDataDir(string root) =>
        Path.Combine(root, "src", "Deckle.Input.Autocorrect", "Data");
}
