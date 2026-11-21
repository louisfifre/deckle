using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Core;

namespace Deckle.Folders.Views;

// "Describe the folder" (spec, Photos; boards 12.4, 12.19): a model on this
// computer goes through the folder's photos and describes each one that has
// none. While it runs, the command gives way to "126 of 412 described", a
// progress bar and a way to stop; "Apply marks…" and "Open in
// darktable" stay usable, and so does "Describe this photo" in the panel
// (Open 27, drawn: the queue puts that photo first).
//
// The run belongs to the app (PhotoDescriptions): leaving the folder or
// closing the window does not stop it, and the folder shows its progress
// again when it is opened. Only "Stop describing the folder" stops it. When it ends, a bar above
// the gallery says how many photos have a description and how many failed;
// a stop says so too, and a failure of the setup says what is missing and
// leads to the setting (Open 33, no board: said where the command is, as
// Explorer says it in its own window).
internal sealed partial class PhotosView
{
    private readonly PhotoDescriptions? _descriptions;
    private PhotoDescriptionRun? _run;

    // The folder the bar of the last run's end is about: it closes when the
    // view shows another folder.
    private string? _resultFolder;

    private async void OnDescribeClick(object sender, RoutedEventArgs e) => await DescribeFolderAsync();

    internal async Task DescribeFolderAsync()
    {
        if (_descriptions is null || _place is not { } place || _tiles.Count == 0) return;
        DescribeResult.IsOpen = false;
        List<PhotoFile> photos = [.. _tiles.Select(t => t.Photo)];
        (PhotoDescriptionRun? run, PhotoDescriptionFailure? notReady) = await _descriptions.StartFolderAsync(place.Path, photos);
        if (_place is null || !FolderPaths.Comparer.Equals(_place.Path, place.Path)) return;
        if (notReady is { } failure)
        {
            ShowSetupFailure(failure);
            return;
        }
        Follow(run);
    }

    private void OnStopDescribingClick(object sender, RoutedEventArgs e) => _run?.Stop();

    // After a folder is shown: the run going on in it, if any, takes the
    // command's place again.
    private void ShowDescribing()
    {
        if (_place is not { } place || !FolderPaths.Comparer.Equals(_resultFolder ?? "", place.Path))
            DescribeResult.IsOpen = false;
        Follow(_place is { } p ? _descriptions?.RunOf(p.Path) : null);
    }

    private void Follow(PhotoDescriptionRun? run)
    {
        if (!ReferenceEquals(_run, run))
        {
            if (_run is not null) _run.Changed -= OnRunChanged;
            _run = run;
            if (run is not null) run.Changed += OnRunChanged;
        }
        ShowRun();
    }

    // Raised on the queue's thread; the view may have moved to another
    // folder or another window by the time it lands.
    private void OnRunChanged(PhotoDescriptionRun run) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(run, _run)) return;
            ShowRun();
        });

    private void ShowRun()
    {
        bool hasPhotos = _descriptions is not null && _tiles.Count > 0;
        if (_run is { IsRunning: true } run)
        {
            int described = run.Described;
            DescribingText.Text = FoldersText.Format("Photos_Describe_Progress_Format", described, run.Total);
            DescribingBar.Maximum = Math.Max(1, run.Total);
            DescribingBar.Value = described;
            Describing.Visibility = Visibility.Visible;
            DescribeButton.Visibility = Visibility.Collapsed;
            return;
        }

        Describing.Visibility = Visibility.Collapsed;
        DescribeButton.Visibility = hasPhotos ? Visibility.Visible : Visibility.Collapsed;
        if (_run is { } ended)
        {
            ShowRunEnd(ended);
            ended.Changed -= OnRunChanged;
            _run = null;
        }
    }

    private void ShowRunEnd(PhotoDescriptionRun run)
    {
        if (run.State == PhotoDescriptionRunState.Failed && run.SetupFailure is { } setup)
        {
            ShowSetupFailure(setup);
            return;
        }

        DescribeResult.ActionButton = null;
        if (run.State == PhotoDescriptionRunState.Stopped)
        {
            DescribeResult.Severity = InfoBarSeverity.Informational;
            DescribeResult.Title = FoldersText.Format("Photos_Describe_Stopped_Format", run.Described, run.Total);
            DescribeResult.Message = "";
        }
        else
        {
            DescribeResult.Severity = run.Failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            DescribeResult.Title = FoldersText.Format("Photos_Describe_Done_Format", run.Described, run.Total);
            DescribeResult.Message = run.Failed > 0 ? PhotoText.Count("Photos_Describe_Failed", run.Failed) : "";
        }
        OpenResult();
    }

    // The same sentence as the panel's, with the way to the setting.
    private void ShowSetupFailure(PhotoDescriptionFailure failure)
    {
        DescribeResult.Severity = InfoBarSeverity.Warning;
        DescribeResult.Title = FoldersText.Get("Photos_Describe_NotDone");
        DescribeResult.Message = PhotoDescriptionText.Failure(failure);
        DescribeResult.ActionButton = null;
        if (_descriptions?.OpenSettings is { } open)
        {
            var button = new Button { Content = FoldersText.Get("Photos_Description_Settings") };
            button.Click += (_, _) => open();
            DescribeResult.ActionButton = button;
        }
        OpenResult();
    }

    private void OpenResult()
    {
        _resultFolder = _place?.Path;
        DescribeResult.IsOpen = true;
    }

    // A view out of the window (another tool shown, the window closed) stops
    // listening; ShowAsync follows the run again when the view comes back.
    private void ListenToRunWhileLoaded() => Unloaded += (_, _) => Follow(null);
}
