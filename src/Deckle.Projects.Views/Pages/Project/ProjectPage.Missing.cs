using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// A project whose folder is gone (spec): its page shows the name, the path and
// one sentence; no readouts, no links, no actions. Refresh brings the page back
// when the folder is back, since every refresh reads the folder again.
internal sealed partial class ProjectPage
{
    // What the disk said about the folder before any reading existed: whether
    // it is there and whether it holds the script menu.
    private sealed record DiskProbe(bool FolderExists, bool HasMenu);

    // The probe, made once off the UI thread when the page opens on a project
    // that has no reading yet (Home usually read it already). The page never
    // asks the disk on the UI thread, and the one-second tick not at all.
    private DiskProbe? _probe;
    private Task _probing = Task.CompletedTask;

    private void ProbeDiskIfUnread(ProjectEntry project)
    {
        if (_context.Workspace.Readings.Get(project.Id) is not null) return;

        string path = project.Path;
        _probing = Task.Run(() =>
        {
            _probe = new DiskProbe(Directory.Exists(path), File.Exists(Path.Combine(path, "scripts", "menu.ps1")));
            _context.Dispatch(() =>
            {
                if (!_disposed) Apply();
            });
        });
    }

    // Whether nothing says yet what the folder holds: no reading and the probe
    // is still running. The page then shows its header alone, so a missing
    // folder does not flash the whole page.
    private bool FolderIsUnknown(ProjectEntry project) =>
        _context.Workspace.Readings.Get(project.Id) is null && _probe is null;

    // The reading says so once it has been made, the probe before that.
    private bool FolderIsMissing(ProjectEntry project)
    {
        ProjectReading? reading = _context.Workspace.Readings.Get(project.Id);
        return reading is null ? _probe is { FolderExists: false } : reading.Folder == FolderState.Missing;
    }

    private void ApplyMissing(bool missing)
    {
        MissingText.Text = ProjectsText.Get("Project_Missing");
        MissingText.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;

        // The pinned section shows itself when the menu has answered
        // (ApplyPinned); a missing folder hides it here.
        Visibility rest = missing ? Visibility.Collapsed : Visibility.Visible;
        Readouts.Visibility = rest;
        OpenIn.Visibility = rest;
        if (missing) Pinned.Visibility = Visibility.Collapsed;
    }
}
