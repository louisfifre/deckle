namespace Deckle.Folders.App;

// Deckle Folders was built under the code name "atelier", so its data lived in
// <suite root>\apps\atelier (its module folder in modules\atelier). The app is
// now "folders" and reads <suite root>\apps\folders. This moves the old folder
// to the new place once, so the folders a person added, their icons and their
// photo-description model choice survive the rename. Data older than the
// separate app, still in the suite's own modules\atelier, moves the same way.
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

    public static FoldersDataMigrationResult Run(string suiteRoot) => Run(suiteRoot, Directory.Move);

    // The move is a parameter so a test can fail one of the two moves alone.
    public static FoldersDataMigrationResult Run(string suiteRoot, Action<string, string> move)
    {
        string oldRootName = Path.Combine(AppsFolderName, OldAppId);
        string newRootName = Path.Combine(AppsFolderName, NewAppId);
        string oldRoot = Path.Combine(suiteRoot, oldRootName);
        string newRoot = Path.Combine(suiteRoot, newRootName);

        if (!Directory.Exists(oldRoot))
            return MoveSuiteModuleFolder(suiteRoot, newRootName, move);

        // Both exist: moving would merge two histories, so nothing moves and a
        // person decides which one to keep.
        if (Directory.Exists(newRoot))
            return new(FoldersDataMigrationOutcome.BothExist, null, oldRootName, newRootName);

        try
        {
            move(oldRoot, newRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A second copy racing for the same move, a file held open, a
            // denied permission: nothing moved, the app starts on an empty
            // data root and the old folder is still where it was.
            return new(FoldersDataMigrationOutcome.Failed, ex, oldRootName, newRootName);
        }

        string oldModule = Path.Combine(newRoot, ModulesFolderName, OldAppId);
        string newModule = Path.Combine(newRoot, ModulesFolderName, NewAppId);
        if (!Directory.Exists(oldModule))
            return new(FoldersDataMigrationOutcome.Moved, null, oldRootName, newRootName);

        // A module folder already named "folders" holds data of its own:
        // leave both, the warning says so.
        if (Directory.Exists(newModule))
            return new(FoldersDataMigrationOutcome.MovedModuleFolderKept, null, oldRootName, newRootName);

        try
        {
            move(oldModule, newModule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The data root already moved: it is not "still where it was".
            // Only the module folder kept its old name, and the module reads
            // the new one, so its data is out of reach until it is renamed.
            return new(FoldersDataMigrationOutcome.MovedModuleFolderNotRenamed, ex, oldRootName, newRootName);
        }
        return new(FoldersDataMigrationOutcome.Moved, null, oldRootName, newRootName);
    }

    // Before the module left the published Deckle, it ran inside Deckle and kept
    // its data in the suite's own module folder, <suite root>\modules\atelier.
    // A machine that never ran the separate app before this rename still has its
    // data there, so it moves into the new app's module folder.
    private static FoldersDataMigrationResult MoveSuiteModuleFolder(
        string suiteRoot, string newRootName, Action<string, string> move)
    {
        string oldName = Path.Combine(ModulesFolderName, OldAppId);
        string newName = Path.Combine(newRootName, ModulesFolderName, NewAppId);
        string oldModule = Path.Combine(suiteRoot, oldName);
        string newModule = Path.Combine(suiteRoot, newName);

        if (!Directory.Exists(oldModule))
            return new(FoldersDataMigrationOutcome.NothingToMove, null, oldName, newName);

        // The new app already keeps data of its own: nothing moves.
        if (Directory.Exists(newModule))
            return new(FoldersDataMigrationOutcome.BothExist, null, oldName, newName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newModule)!);
            move(oldModule, newModule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(FoldersDataMigrationOutcome.Failed, ex, oldName, newName);
        }
        return new(FoldersDataMigrationOutcome.Moved, null, oldName, newName);
    }

    private const string AppsFolderName = "apps";
}
public enum FoldersDataMigrationOutcome
{
    NothingToMove,
    Moved,
    // The data root moved, but modules\atelier stayed beside an existing modules\folders.
    MovedModuleFolderKept,
    // The data root moved, but renaming modules\atelier failed: it keeps its old name.
    MovedModuleFolderNotRenamed,
    // apps\atelier and apps\folders both exist: nothing was moved.
    BothExist,
    Failed,
}

// From and To name the moved folders relative to the suite root, for the log.
public sealed record FoldersDataMigrationResult(
    FoldersDataMigrationOutcome Outcome, Exception? Error, string From = "", string To = "");
