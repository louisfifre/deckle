namespace Deckle.Projects;

// The meta line of a project's card and of its row on a category page (spec,
// Home, "A card"): the version, then "N versions found" when the versions
// differ, then "N uncommitted" only when N is above zero.
//
//   v0.131.0 · 3 uncommitted
//   v0.6.0
//   No version yet
//   v0.32.0 · 3 versions found
//   v0.32.0 · 3 versions found · 2 uncommitted
//   This folder was not found
//
// A reading that is not there yet gives an empty line: on a first read the card
// shows nothing rather than a guess (spec, Readouts).
public static class CardMeta
{
    public const string NoVersionYet = "No version yet";
    public const string FolderNotFound = "This folder was not found";

    public static string Text(ProjectReading? reading)
    {
        if (reading is null) return "";
        if (reading.Folder == FolderState.Missing) return FolderNotFound;

        List<string> parts = [reading.Version.Tag ?? NoVersionYet];
        // The count of versions comes before the uncommitted files.
        if (reading.Version.ShowsCount) parts.Add($"{reading.Version.Count} versions found");
        if (reading.UncommittedFiles is > 0) parts.Add($"{reading.UncommittedFiles} uncommitted");
        return string.Join(" · ", parts);
    }
}
