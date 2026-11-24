namespace Deckle.Projects.App;

// Before this app, the Projects window lived in the published Deckle, which
// keeps its data in the suite's own module folder, <suite root>\modules\projects
// (projects.json: the projects, their categories and pins; terminal.json: the
// terminal choice). This app reads <suite root>\apps\projects\modules\projects.
// On its first start it copies the files of the old folder there, so the
// projects a person added are on its Home.
//
// A copy, not a move as Deckle Folders did: the Deckle running on the
// maintainer's machine still carries the Projects window until its next
// update, and moving the files from under it would empty its list and make it
// write a new one. The old folder is left as it was; from the copy on, the two
// lists are separate and this app's is the one that counts.
//
// Only the files at the top of the folder: "runs" holds the wrappers and exit
// files of actions the published Deckle started and follows, which mean
// nothing to another process.
//
// It runs in Program.Main, before anything reads AppPaths: reading it creates
// the new data root, though not the module folder, which is what makes the
// copy happen once. Pure over a suite root, so a test hands it a temp folder.
// Remove this file, its test and its call in Program.Main once the
// maintainer's machine has copied.
public static class ProjectsDataCopy
{
    public const string ModuleId = "projects";

    public static ProjectsDataCopyResult Run(string suiteRoot)
    {
        string oldName = Path.Combine("modules", ModuleId);
        string newName = Path.Combine("apps", Program.AppId, "modules", ModuleId);
        string oldModule = Path.Combine(suiteRoot, oldName);
        string newModule = Path.Combine(suiteRoot, newName);

        // This app already keeps data of its own: nothing is copied, ever again.
        if (Directory.Exists(newModule))
            return new(ProjectsDataCopyOutcome.AlreadyHasData, null, oldName, newName);

        string[] files = Directory.Exists(oldModule) ? Directory.GetFiles(oldModule) : [];
        if (files.Length == 0)
            return new(ProjectsDataCopyOutcome.NothingToCopy, null, oldName, newName);

        // Copied beside the target, then renamed into place: a copy cut short
        // leaves no module folder, and the next start tries again.
        string staging = newModule + ".copying";
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            foreach (string file in files)
                File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
            Directory.Move(staging, newModule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file held open, a denied permission: the app starts on an
            // empty list, and the old folder is still where it was.
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return new(ProjectsDataCopyOutcome.Failed, ex, oldName, newName);
        }
        return new(ProjectsDataCopyOutcome.Copied, null, oldName, newName, files.Length);
    }
}

public enum ProjectsDataCopyOutcome
{
    // The published Deckle kept no Projects data on this machine.
    NothingToCopy,
    Copied,
    // The app's module folder exists: it was copied before, or the app ran.
    AlreadyHasData,
    Failed,
}

// From and To name the folders relative to the suite root, for the log.
public sealed record ProjectsDataCopyResult(
    ProjectsDataCopyOutcome Outcome, Exception? Error, string From = "", string To = "", int Files = 0);
