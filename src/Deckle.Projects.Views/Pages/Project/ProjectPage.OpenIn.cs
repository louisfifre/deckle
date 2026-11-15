using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// "Open in": where the person goes to work on the project (spec, Open in).
internal sealed partial class ProjectPage
{
    private void ApplyOpenIn(ProjectEntry project)
    {
        List<OpenInLink> links = [];

        // "Deckle Folders" is a hook the host hands the window: Deckle Projects
        // does not know the window of Deckle Folders. No hook, no button.
        if (_context.Hooks.OpenInFolders is { } openInFolders)
        {
            links.Add(new OpenInLink("folders", ProjectsGlyphs.Library, ProjectsText.Get("Project_OpenInFolders"),
                () => _ = RunHookAsync(openInFolders, project.Path)));
        }

        links.Add(new OpenInLink("explorer", ProjectsGlyphs.FolderOpen, ProjectsText.Get("Project_OpenInExplorer"),
            () => _context.Workspace.Opener.OpenInExplorer(project.Path)));
        links.Add(new OpenInLink("terminal", ProjectsGlyphs.Keyboard, ProjectsText.Get("Project_OpenInTerminal"),
            () => _context.Workspace.Opener.OpenInTerminal(project.Path)));

        // The remote's page, named after its host, only when the remote has one.
        // "Deckle Agents" joins the row once it exists, not before.
        if (_context.Workspace.Readings.Get(project.Id)?.Remote is { } remote)
        {
            links.Add(new OpenInLink("remote", ProjectsGlyphs.Globe, remote.HostName,
                () => _context.Workspace.Opener.OpenWebPage(remote.WebPage),
                ProjectsText.Format("Project_OpenInHost_Format", remote.HostName)));
        }

        OpenIn.Apply(links);
    }

    private async Task RunHookAsync(Func<string, Task> hook, string path)
    {
        try
        {
            await hook(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The neighbour's window failed to open: this page goes on, and the
            // log says what kind of failure it was.
            DeckleProjectsSource.Log.ProjectsPageFailed("project", ex.GetType().Name);
        }
    }
}
