namespace Deckle.Folders.App;

// Deckle Folders was built under the code name "atelier", so its data lived in
// <suite root>\apps\atelier (its module folder in modules\atelier). The app is
// now "folders" and reads <suite root>\apps\folders. This moves the old folder
// to the new place once, so the folders a person added, their icons and their
// photo-description model choice survive the rename.
//
// It runs in Program.Main, before anything reads AppPaths: reading it creates
// the new data root, and a data root that already exists is what makes the
// move refuse. It touches nothing it was not asked to: the old diagnostics
// folder (diagnostics\atelier) stays where it is, it is only a log.
//
// Pure over a suite root, so a test hands it a temp folder. Remove this file,
// its test and its call in Program.Main once the maintainer's machine has
// migrated.
public static class FoldersDataMigration
{
    public const string OldAppId = "atelier";
    public const string NewAppId = "folders";

    // The module folder inside the data root, named after the module id.
    private const string ModulesFolderName = "modules";

    public static FoldersDataMigrationResult Run(string suiteRoot)
    {
        string appsRoot = Path.Combine(suiteRoot, "apps");
        string oldRoot = Path.Combine(appsRoot, OldAppId);
        string newRoot = Path.Combine(appsRoot, NewAppId);

        if (!Directory.Exists(oldRoot))
            return new FoldersDataMigrationResult(FoldersDataMigrationOutcome.NothingToMove, null);

        // Both exist: moving would merge two histories, so nothing moves and a
        // person decides which one to keep.
        if (Directory.Exists(newRoot))
            return new FoldersDataMigrationResult(FoldersDataMigrationOutcome.BothExist, null);

        try
        {
            Directory.Move(oldRoot, newRoot);

            string oldModule = Path.Combine(newRoot, ModulesFolderName, OldAppId);
            string newModule = Path.Combine(newRoot, ModulesFolderName, NewAppId);
            if (Directory.Exists(oldModule))
            {
                // A module folder already named "folders" holds data of its
                // own: leave both, the warning says so.
                if (Directory.Exists(newModule))
                    return new FoldersDataMigrationResult(FoldersDataMigrationOutcome.MovedModuleFolderKept, null);
                Directory.Move(oldModule, newModule);
            }
            return new FoldersDataMigrationResult(FoldersDataMigrationOutcome.Moved, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A second copy racing for the same move, a file held open, a
            // denied permission: the app starts on an empty data root and the
            // old folder is still where it was.
            return new FoldersDataMigrationResult(FoldersDataMigrationOutcome.Failed, ex);
        }
    }
}

public enum FoldersDataMigrationOutcome
{
    NothingToMove,
    Moved,
    // The data root moved, but modules\atelier stayed beside an existing modules\folders.
    MovedModuleFolderKept,
    // apps\atelier and apps\folders both exist: nothing was moved.
    BothExist,
    Failed,
}

public sealed record FoldersDataMigrationResult(FoldersDataMigrationOutcome Outcome, Exception? Error);
