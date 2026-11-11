using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// "Appliquer les marques…" (spec, Behaviours: the marks are applied in one
// step, after a summary; boards 12.6 and 12.22). The plan is made off the UI
// thread, the summary says what will happen, and only its main button runs
// it: the photos to delete go to the Windows Recycle Bin with their sidecars,
// one outcome per file. Afterwards the folder is read again, so the recycled
// photos leave the gallery and the counter of photos to delete reads 0.
//
// What could not be done is named, file by file, in a bar above the gallery;
// a run where every photo reached the bin says nothing, as 12.22 draws it
// (Open 26, drawn: nothing says where the photos went).
internal sealed partial class PhotosView
{
    private bool _applying;
    private ContentDialog? _summary;

    // Raised when the summary is on screen, for the self-check to photograph it.
    internal event Action? SummaryOpened;

    private async void OnApplyClick(object sender, RoutedEventArgs e) => await ApplyAsync();

    internal async Task ApplyAsync()
    {
        if (_place is not { } place || _store is not { } store || _applying || _summary is not null) return;
        List<PhotoFile> photos = [.. _tiles.Select(t => t.Photo)];
        MarksPlan plan = await _queue.Run(() => MarksApplier.Plan(place.Path, photos, store));
        if (plan.IsEmpty || XamlRoot is null) return;

        _summary = ApplySummary.Create(plan, XamlRoot, _host.Icons);
        _summary.Opened += (_, _) => SummaryOpened?.Invoke();
        ContentDialogResult answer;
        try
        {
            answer = await _summary.ShowAsync();
        }
        finally
        {
            _summary = null;
        }
        if (answer != ContentDialogResult.Primary) return;
        await ExecuteAsync(place, store, plan);
    }

    internal void CloseSummary() => _summary?.Hide();

    private async Task ExecuteAsync(FolderPlace place, PhotoMarkStore store, MarksPlan plan)
    {
        _applying = true;
        ApplyResult.IsOpen = false;
        int total = plan.Recycles.Count + plan.Renames.Renames.Count;
        ApplyingBar.Maximum = total;
        ApplyingBar.Value = 0;
        ApplyingText.Text = AtelierText.Format("Photos_Applying_Format", 0, total);
        Applying.Visibility = Visibility.Visible;
        ShowCounts();

        // Progress<T> made here posts its reports back to the UI thread.
        var progress = new Progress<int>(done =>
        {
            ApplyingBar.Value = done;
            ApplyingText.Text = AtelierText.Format("Photos_Applying_Format", done, total);
        });
        MarksOutcome outcome;
        try
        {
            outcome = await _queue.Run(() => MarksApplier.Execute(plan, store, new ShellRecycleBin(), progress));
        }
        finally
        {
            _applying = false;
            Applying.Visibility = Visibility.Collapsed;
        }

        ShowOutcome(outcome);
        if (_place is not null && FolderPaths.Comparer.Equals(_place.Path, place.Path))
            await ShowAsync(place, SelectedTiles().FirstOrDefault()?.Photo.Path, CancellationToken.None);
    }

    // A photo that stayed in its folder is a warning: nothing was lost and the
    // mark is still on it, so applying again retries it. A photo destroyed
    // instead of recycled is an error, said plainly; the bin's guard should
    // never let it happen. A photo already gone when its turn came needs no word.
    private void ShowOutcome(MarksOutcome outcome)
    {
        List<string> stayed = [.. outcome.Recycles
            .Where(r => r.Outcome.Status is RecycleStatus.Refused or RecycleStatus.Failed or RecycleStatus.NotDone)
            .Select(r => r.Photo.Name)];
        List<string> destroyed = [.. outcome.Recycles
            .Where(r => r.Outcome.Status == RecycleStatus.DeletedForGood)
            .Select(r => r.Photo.Name)];

        if (destroyed.Count > 0)
        {
            ApplyResult.Severity = InfoBarSeverity.Error;
            ApplyResult.Title = PhotoText.Count("Photos_Destroyed", destroyed.Count);
            ApplyResult.Message = PhotoText.Names(destroyed);
            ApplyResult.IsOpen = true;
        }
        else if (stayed.Count > 0)
        {
            ApplyResult.Severity = InfoBarSeverity.Warning;
            ApplyResult.Title = PhotoText.Count("Photos_Stayed", stayed.Count);
            ApplyResult.Message = PhotoText.Names(stayed);
            ApplyResult.IsOpen = true;
        }
    }
}
