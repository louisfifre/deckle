using System.ComponentModel;
using System.Diagnostics;

namespace Deckle.Projects;

// Opens a project's links. File Explorer and the web page go through the shell
// (Process.Start with UseShellExecute), the terminal and the script menu through
// the terminal launcher: the same one the actions use, so the app has one
// terminal.
//
// ShellFileLauncher of Deckle Folders is not reused: it checks File.Exists, and
// a project is a directory.
public sealed class ProjectOpener(ITerminalLauncher terminal, string? shell, IProcessStarter? starter = null) : IProjectOpener
{
    private readonly IProcessStarter _starter = starter ?? new SystemProcessStarter();

    public bool OpenInExplorer(string projectPath)
    {
        if (!Directory.Exists(projectPath)) return Failed("explorer", "folder-missing");
        return Shell("explorer", new ProcessStartInfo(projectPath) { UseShellExecute = true });
    }

    public bool OpenInTerminal(string projectPath)
    {
        if (!Directory.Exists(projectPath)) return Failed("terminal", "folder-missing");
        return Terminal("terminal", new TerminalCommand(projectPath, Title: "Terminal", shell, ScriptText: null));
    }

    public bool OpenScriptMenu(string projectPath)
    {
        string menu = Path.Combine(projectPath, "scripts", "menu.ps1");
        if (!Directory.Exists(projectPath)) return Failed("script-menu", "folder-missing");
        if (!File.Exists(menu)) return Failed("script-menu", "no-menu");
        if (shell is null) return Failed("script-menu", "no-shell");
        return Terminal("script-menu", new TerminalCommand(projectPath, Title: "Script menu", shell, RunWrapper.BuildMenu(shell, menu)));
    }

    public bool OpenWebPage(Uri page)
    {
        // A page address is built from a remote's text: nothing but https is
        // handed to the shell, whatever it was built from.
        if (page.Scheme != Uri.UriSchemeHttps) return Failed("web-page", "not-https");
        return Shell("web-page", new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    private bool Shell(string kind, ProcessStartInfo start)
    {
        try
        {
            // The process, when the shell hands one back, is the other
            // program's own: nothing here waits on it.
            _starter.Start(start)?.Dispose();
            DeckleProjectsSource.Log.ProjectLinkOpened(kind);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Failed(kind, "shell-error");
        }
    }

    private bool Terminal(string kind, TerminalCommand command)
    {
        TerminalStart start = terminal.Launch(command);
        if (!start.Started) return Failed(kind, start.FailureReason ?? "terminal-unavailable");
        DeckleProjectsSource.Log.ProjectLinkOpened(kind);
        return true;
    }

    private static bool Failed(string kind, string reason)
    {
        DeckleProjectsSource.Log.ProjectLinkFailed(kind, reason);
        return false;
    }
}
