using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// ── InterfaceCaptureRun ───────────────────────────────────────────────────────
//
// One interface capture of the developer mode: a dated folder under
// <UserDataRoot>\diagnostics\interface-captures\, one PNG per window or
// settings page (WindowSnapshot, InterfaceCaptureNames), and an index.json
// that says what each file shows (InterfaceCaptureIndex).
//
// The run knows nothing of Deckle's windows: the App hands it each window
// handle with a name, and the Settings window walks its own pages between two
// calls. That keeps the capture here, in the module that owns the Direct3D
// device, and the knowledge of the windows where the windows are.
//
// One window that fails does not stop the run: the failure is logged, listed
// in the index, and the next window is captured. Begin, then the captures,
// then Complete, all on the UI thread.
//
// The images show whatever the windows show, the Log window's lines and a
// transcript included. They stay on this machine like the logs beside them;
// handing them to a remote reviewer is the person's decision.
public sealed class InterfaceCaptureRun
{
    public static string CapturesDirectory
        => Path.Combine(AppPaths.DiagnosticsDirectory, "interface-captures");

    private readonly string _workId = WorkId.Next();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);
    private readonly InterfaceCaptureIndex _index;

    private InterfaceCaptureRun(string directory, DateTimeOffset startedAt)
    {
        Directory = directory;
        _index = new InterfaceCaptureIndex(startedAt);
    }

    public string Directory { get; }
    public int Captured => _index.Captures.Count;
    public int Failed => _index.Failures.Count;

    // The file the last CaptureAsync wrote, relative to Directory; null when it
    // failed. Lets a caller that reports per image name the file.
    public string? LastFile { get; private set; }

    // Throws when the folder cannot be made, after logging the run's start,
    // its stop and its end.
    public static InterfaceCaptureRun Begin(DateTimeOffset startedAt)
        => Begin(startedAt, Path.Combine(CapturesDirectory, InterfaceCaptureNames.FolderName(startedAt)));

    // The same run in a folder the caller names, for a run whose images must
    // outlive the data root (the self-check writes them beside its report).
    public static InterfaceCaptureRun Begin(DateTimeOffset startedAt, string directory)
    {
        var run = new InterfaceCaptureRun(directory, startedAt);
        DeckleVisionSource.Log.InterfaceCaptureStarted(run._workId, RelativeToDataRoot(directory));
        try
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            run.Stop("create_folder", ex, writeIndex: false);
            throw;
        }
        return run;
    }

    // The run broke on a step of its own, not on one window: logs the cause,
    // then ends the run with the step as its outcome, the index written with
    // what was captured so far.
    public void Stop(string step, Exception ex) => Stop(step, ex, writeIndex: true);

    private void Stop(string step, Exception ex, bool writeIndex)
    {
        DeckleVisionSource.Log.InterfaceCaptureStopped(_workId, step, ex.GetType().Name, ex.Message);
        if (writeIndex) Complete(step);
        else DeckleVisionSource.Log.InterfaceCaptureCompleted(
            _workId, step, Captured, Failed, _clock.ElapsedMilliseconds);
    }

    // Captures one window. window is its stable name ("log", "settings"); page
    // and pageLabel name one page of a paged window, the slug for the file and
    // the rail label for the index. Returns whether an image was written.
    public async Task<bool> CaptureAsync(nint hwnd, string window, string? page = null, string? pageLabel = null)
    {
        string name = InterfaceCaptureNames.Unique(InterfaceCaptureNames.BaseName(window, page), _taken);
        string file = name + InterfaceCaptureNames.FileExtension;
        string path = Path.Combine(Directory, file);
        var clock = Stopwatch.StartNew();
        LastFile = null;
        try
        {
            (int width, int height) = await WindowSnapshot.CaptureToPngAsync(hwnd, path);
            string title = WindowSnapshotInterop.GetTitle(hwnd);
            _index.Add(new InterfaceCaptureEntry(file, InterfaceCaptureNames.Slug(window), pageLabel, title, width, height));
            DeckleVisionSource.Log.InterfaceWindowCaptured(
                _workId, RelativeToDataRoot(path), width, height, clock.ElapsedMilliseconds);
            LastFile = file;
            return true;
        }
        catch (WindowSnapshotException ex)
        {
            Exception cause = ex.InnerException ?? ex;
            _index.Add(new InterfaceCaptureFailure(name, ex.Step, cause.GetType().Name));
            DeckleVisionSource.Log.InterfaceWindowCaptureFailed(
                _workId, name, ex.Step, cause.GetType().Name, cause.Message);
            return false;
        }
    }

    // Writes index.json and closes the run in the log. outcome overrides the
    // computed one when the caller stopped the run on an error of its own.
    public void Complete(string? outcome = null)
    {
        try
        {
            File.WriteAllText(Path.Combine(Directory, InterfaceCaptureNames.IndexFileName), _index.ToJson());
        }
        catch (Exception ex)
        {
            DeckleVisionSource.Log.InterfaceWindowCaptureFailed(
                _workId, InterfaceCaptureNames.IndexFileName, "write_index", ex.GetType().Name, ex.Message);
            outcome ??= "write_index";
        }

        outcome ??= Failed == 0 ? "completed" : Captured == 0 ? "failed" : "partial";
        DeckleVisionSource.Log.InterfaceCaptureCompleted(
            _workId, outcome, Captured, Failed, _clock.ElapsedMilliseconds);
    }

    // Every visible top-level window of this process, for the App to name.
    public static IReadOnlyList<nint> VisibleWindows() => WindowSnapshotInterop.VisibleWindowsOfThisProcess();

    public static string TitleOf(nint hwnd) => WindowSnapshotInterop.GetTitle(hwnd);

    private static string RelativeToDataRoot(string path) => Path.GetRelativePath(AppPaths.UserDataRoot, path);
}
