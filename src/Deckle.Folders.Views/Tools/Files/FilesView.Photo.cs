using Deckle.Core;

namespace Deckle.Folders.Views;

// The panel of a photo selected in the file list: the photo tool's own panel
// (spec, Details panel: "a photo selected in the file list shows its data";
// 12.15: the same as in the photo tool, without the folder's counters, which
// sit above the photo tool's gallery and not in its panel). Its mark buttons
// and its G and X keys work here too, on the photo tool's store, so a mark set
// in one tool is the mark the other shows.
//
// The store is the photo tool's: one small file per folder in the module's
// data folder, read and written through the queue both tools share, so a
// mark set here and the photo tool's next look at the folder come in order.
// Each use opens the store afresh, so a mark the other tool set since is read.
internal sealed partial class FilesView
{
    private readonly string _moduleDirectory;
    private readonly PhotoMarkQueue _marks;

    // The app's descriptions, the photo tool's own: the panel shows and asks
    // for the same description in both tools. Null when the app has no model.
    private readonly PhotoDescriptions? _descriptions;

    // One reading of each photo's capture data per folder visit, as in the
    // photo tool.
    private readonly Dictionary<string, CaptureReading> _readings = new(FolderPaths.Comparer);

    private CancellationTokenSource? _panelLoading;
    private PhotoTile? _photoTile;
    private PhotoDetails? _photoDetails;

    private static bool IsPhoto(FileRow row) => !row.Entry.IsFolder && PhotoFormats.IsPhoto(row.Entry.Name);

    private void ForgetPhotoPanel()
    {
        _panelLoading?.Cancel();
        _panelLoading = null;
        _photoTile = null;
        _photoDetails = null;
    }

    private async Task ShowPhotoDetailsAsync(FileRow row)
    {
        if (_place is not { } place) return;
        var loading = new CancellationTokenSource();
        _panelLoading = loading;

        FolderEntry entry = row.Entry;
        var photo = new PhotoFile(entry.Name, entry.Path, entry.Size ?? 0, entry.Modified);
        string folder = place.Path;
        string moduleDirectory = _moduleDirectory;
        Task<PhotoMark> mark = _marks.Run(() => new PhotoMarkStore(moduleDirectory, folder).MarkOf(photo.Name));
        if (!_readings.TryGetValue(photo.Path, out CaptureReading? reading))
        {
            reading = await Task.Run(() => CaptureDataReader.Read(photo.Path));
            _readings[photo.Path] = reading;
        }
        PhotoMark current = await mark;
        if (loading.IsCancellationRequested) return;

        var tile = new PhotoTile(photo, current);
        _photoTile = tile;
        _photoDetails = PhotoDetails.For(tile, reading, MarkPhoto, large: false, rank: 0, total: 0, _descriptions);
        _host.Select(photo.Path, _photoDetails.Element);
    }

    // As in the photo tool: pressing the mark the photo already carries takes
    // it off. The panel changes at once and the store follows on the queue.
    private void MarkPhoto(PhotoMark mark)
    {
        if (_photoTile is not { } tile || _place is not { } place) return;
        PhotoMark next = tile.Mark == mark ? PhotoMark.None : mark;
        tile.Mark = next;
        _photoDetails?.ShowMark(next);

        string folder = place.Path;
        string moduleDirectory = _moduleDirectory;
        string name = tile.Name;
        _ = _marks.Run(() => new PhotoMarkStore(moduleDirectory, folder).Mark(name, next));
    }
}
