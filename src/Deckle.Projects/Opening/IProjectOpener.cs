namespace Deckle.Projects;

// The links that open a project in another tool (spec, Open in). "Deckle
// Folders" is not here: it is a hook the host hands the window (ProjectsHooks),
// because Deckle Projects does not know the window of Deckle Folders.
//
// Every method answers whether the link opened; nothing throws for a folder that
// is gone or a refusal of the system, the page goes on.
public interface IProjectOpener
{
    bool OpenInExplorer(string projectPath);

    // The terminal of the app, in the project's folder, with nothing running.
    bool OpenInTerminal(string projectPath);

    // The script menu itself, in a terminal: where the person picks any script.
    bool OpenScriptMenu(string projectPath);

    // Only an https address is opened.
    bool OpenWebPage(Uri page);
}
