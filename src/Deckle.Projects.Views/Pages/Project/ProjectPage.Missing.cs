using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// A project whose folder is gone (spec): its page shows the name, the path and
// one sentence; no readouts, no links, no actions. Refresh brings the page back
// when the folder is back, since every refresh reads the folder again.
internal sealed partial class ProjectPage
{
    // The reading says so once it has been made; before that the folder is
    // looked for, so a first look at a missing folder does not flash the whole
    // page.
    private bool FolderIsMissing(ProjectEntry project)
    {
        ProjectReading? reading = _context.Workspace.Readings.Get(project.Id);
        return reading is null ? !Directory.Exists(project.Path) : reading.Folder == FolderState.Missing;
    }

    private void ApplyMissing(bool missing)
    {
        MissingText.Text = ProjectsText.Get("Project_Missing");
        MissingText.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;

        Visibility rest = missing ? Visibility.Collapsed : Visibility.Visible;
        Readouts.Visibility = rest;
        OpenIn.Visibility = rest;
        Pinned.Visibility = rest;
    }
}
