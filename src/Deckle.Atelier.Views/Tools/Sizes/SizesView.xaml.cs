using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Atelier.Views;

// The sizes tool's centre (spec, Sizes; boards 12.7, 12.21, 12.23).
//
// How an analysis runs. ShowAsync starts a SizeScan, which reads the disk on
// the thread pool, and stays on the UI thread only to look at it: every tick it
// takes the scan's partial report and updates the rows in place. A folder not
// measured yet shows "…" (12.23). The token the window hands over is cancelled
// when the person goes to another place or another tool, and cancelling it
// stops the scan, so no analysis runs for a list nobody looks at.
//
// When the disk is not read again. The window calls ShowAsync on navigation,
// on a tool switch and on Refresh (F5) alike. A completed report already holds
// its sub-folders' own reports (SizeReport.ForFolder), so going into one of
// them, by a double-click or the address bar, reuses it. Coming back to this
// tool on the same place reuses the report too: the view was unloaded in
// between, which a Refresh never does, and that is how the two are told apart
// without changing the tool seam. Refresh always measures again.
internal sealed partial class SizesView : UserControl, IAtelierToolView
{
    // How often the list follows a running analysis: often enough to read as
    // live, rarely enough that a large folder's rows are not rebuilt for
    // nothing (a snapshot copies the place's row list).
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(200);

    private const string LooseKey = "\u0000loose";

    private readonly IAtelierToolHost _host;
    private readonly ObservableCollection<SizeRow> _rows = [];
    private readonly SizesPanel _panel = new();
    private FolderPlace? _place;

    // The report on screen, and the report of the last analysis that ran to
    // the end, which the places beneath it are re-rooted from.
    private SizeReport? _report;
    private SizeReport? _measured;

    private SizeScan? _scan;
    private bool _leftSinceShown;

    // The heavy file picked in the panel, shown under its folder's row.
    private HeavyFile? _heavy;

    // The selection to show once the analysis ends: the window's when the
    // folder was shown, then the person's own as they click.
    private string? _wanted;

    // Set while the rows are changed by the view, so the selection changes the
    // list makes by itself are not taken for the person's.
    private bool _filling;

    public SizesView(IAtelierToolHost host)
    {
        _host = host;
        InitializeComponent();
        Rows.ItemsSource = _rows;
        HeaderName.Text = AtelierText.Get("Sizes_HeaderName");
        HeaderSize.Text = AtelierText.Get("Sizes_HeaderSize");
        StopButton.Content = AtelierText.Get("Sizes_Stop");
        AutomationProperties.SetName(Progress, AtelierText.Get("Sizes_Progress_Name"));
        AutomationProperties.SetName(Rows, AtelierText.Get("Tool_Sizes"));
        _panel.HeavyPicked += OnHeavyPicked;
        Unloaded += (_, _) => _leftSinceShown = true;
    }

    FrameworkElement IAtelierToolView.Content => this;

    public async Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation)
    {
        bool cameBack = _leftSinceShown;
        _leftSinceShown = false;
        _scan?.Cancel();

        SizeReport? reuse = Reusable(place, cameBack);
        bool newPlace = _place is null || !FolderPaths.Comparer.Equals(_place.Path, place.Path);
        _place = place;
        if (newPlace)
        {
            _heavy = null;
            Replace([]);
        }
        _wanted = selection;

        if (reuse is not null)
        {
            Show(reuse, progress: null);
            return;
        }

        // The panel is about the folder from the start and fills in as the
        // analysis goes (12.23).
        _host.Select(selection, _panel);
        SizeScan scan = SizeScan.Start(place.Path);
        _scan = scan;
        using CancellationTokenRegistration stop = cancellation.Register(scan.Cancel);
        while (!scan.Completion.IsCompleted)
        {
            Show(scan.Snapshot(), scan.Progress);
            await Task.WhenAny(scan.Completion, Task.Delay(Tick));
            if (cancellation.IsCancellationRequested) return;
        }

        SizeReport report = await scan.Completion;
        if (cancellation.IsCancellationRequested || !ReferenceEquals(_scan, scan)) return;
        _scan = null;
        if (report.IsComplete) _measured = report;
        Show(report, progress: null);
    }

    // A report that already says what this place weighs, or null to measure.
    private SizeReport? Reusable(FolderPlace place, bool cameBack)
    {
        if (_report is { IsComplete: true } shown && FolderPaths.Comparer.Equals(shown.Place, place.Path))
            return cameBack ? shown : null;
        return _measured?.ForFolder(place.Path);
    }

    private void Show(SizeReport report, SizeProgress? progress)
    {
        _report = report;
        ShowStatus(report, progress);
        ShowRows(report);
        _panel.Show(report, FolderPaths.DisplayName(report.Place));

        // The selection is shown once the list stops moving; while the
        // analysis runs, a row the person selects keeps its selection as it
        // moves up the list.
        if (report.State != SizeScanState.Running) ShowSelection(report, _wanted);
    }

    private void ShowStatus(SizeReport report, SizeProgress? progress)
    {
        bool running = report.State == SizeScanState.Running;
        string total = EntryFormat.Size(report.TotalBytes, AtelierText.Culture);
        ShareLine.Text = report.PlaceFailure != FolderListingFailure.None
            ? ""
            : AtelierText.Format(report.IsComplete ? "Sizes_Share_Total_Format" : "Sizes_Share_Measured_Format", total);

        StateLine.Text = report.State switch
        {
            SizeScanState.Running when progress is { ChildFolders: >= 0 } p =>
                AtelierText.Format("Sizes_Progress_Format", p.ChildFoldersMeasured, p.ChildFolders),
            SizeScanState.Running => AtelierText.Get("Sizes_Progress_Reading"),
            SizeScanState.Cancelled => AtelierText.Get("Sizes_Stopped"),
            SizeScanState.Completed when report.FinishedAt is { } at =>
                AtelierText.Format("Sizes_Finished_Format", at.ToLocalTime().ToString("t", AtelierText.Culture)),
            _ => "",
        };

        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (progress is { ChildFolders: > 0 } known)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = (double)known.ChildFoldersMeasured / known.ChildFolders;
        }
        else
        {
            Progress.IsIndeterminate = running;
        }

        long unreadable = report.UnreadableFolders;
        string? notice = report.PlaceUnreadable
            ? AtelierText.Get("Sizes_PlaceUnreadable")
            : unreadable == 1
                ? AtelierText.Get("Sizes_Unreadable_One")
                : unreadable > 1
                    ? AtelierText.Format("Sizes_Unreadable_Many_Format", unreadable.ToString("N0", AtelierText.Culture))
                    : null;
        UnreadableNotice.Message = notice ?? "";
        UnreadableNotice.IsOpen = notice is not null;

        string? message = report.PlaceFailure switch
        {
            FolderListingFailure.NotFound => AtelierText.Get("Files_NotFound"),
            FolderListingFailure.AccessDenied => AtelierText.Get("Files_AccessDenied"),
            FolderListingFailure.None when report.State == SizeScanState.Failed => AtelierText.Get("Sizes_Failed"),
            FolderListingFailure.None when report.IsComplete && report.Folders.Count == 0 && report.LooseFileCount == 0
                => AtelierText.Get("Files_Empty"),
            FolderListingFailure.None => null,
            _ => AtelierText.Get("Files_Unreadable"),
        };
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // The rows of a report, in its order (heaviest first, a folder not measured
    // yet last), with the picked heavy file under the row of its folder.
    private void ShowRows(SizeReport report)
    {
        var wanted = new List<SizeRow>(report.Folders.Count + 2);
        string? heavyUnder = _heavy is null ? null : TopFolderOf(_heavy, report.Place);

        if (report.LooseFileCount > 0)
        {
            SizeRow loose = RowFor(SizeRowKind.LooseFiles, LooseKey, null);
            loose.Name = AtelierText.Format("Sizes_LooseFiles_Format", report.LooseFileCount.ToString("N0", AtelierText.Culture));
            loose.Size = EntryFormat.Size(report.LooseFilesBytes, AtelierText.Culture);
            loose.Share = report.LooseFilesShare;
            wanted.Add(loose);
            if (heavyUnder == LooseKey) wanted.Add(HeavyRow(_heavy!, report));
        }

        foreach (FolderSizeRow folder in report.Folders)
        {
            SizeRow row = RowFor(SizeRowKind.Folder, folder.Path, folder.Path);
            row.Name = folder.Name;
            row.Size = folder.Bytes is long bytes ? EntryFormat.Size(bytes, AtelierText.Culture) : AtelierText.Get("Sizes_Pending");
            row.Share = folder.Share;
            row.Warning = folder.UnreadableFolders switch
            {
                0 => "",
                1 => AtelierText.Get("Sizes_RowUnreadable_One"),
                long n => AtelierText.Format("Sizes_RowUnreadable_Many_Format", n.ToString("N0", AtelierText.Culture)),
            };
            wanted.Add(row);
            if (heavyUnder is not null && FolderPaths.Comparer.Equals(heavyUnder, folder.Path))
                wanted.Add(HeavyRow(_heavy!, report));
        }

        Replace(wanted);
    }

    private SizeRow HeavyRow(HeavyFile file, SizeReport report)
    {
        SizeRow row = RowFor(SizeRowKind.HeavyFile, "\u0000heavy" + file.Path, file.Path);
        row.Name = file.Name;
        row.Size = EntryFormat.Size(file.Size, AtelierText.Culture);
        row.Share = report.TotalBytes <= 0 ? 0 : (double)file.Size / report.TotalBytes;
        // Under its top folder's row, the rest of the way is what is left to
        // say: "HEIC" for Téléphone\HEIC\IMG.heic, nothing when it lies in it.
        string relative = file.FolderRelativeTo(report.Place);
        int slash = relative.IndexOf(System.IO.Path.DirectorySeparatorChar);
        row.Where = slash < 0 ? "" : relative[(slash + 1)..];
        return row;
    }

    // The key of the row a heavy file sits under: the place's own files, or the
    // sub-folder of the place that holds it.
    private static string TopFolderOf(HeavyFile file, string place)
    {
        string relative = file.FolderRelativeTo(place);
        if (relative.Length == 0) return LooseKey;
        int slash = relative.IndexOf(System.IO.Path.DirectorySeparatorChar);
        string top = slash < 0 ? relative : relative[..slash];
        return System.IO.Path.Join(place, top);
    }

    private SizeRow RowFor(SizeRowKind kind, string key, string? path) =>
        _rows.FirstOrDefault(r => r.Key == key) ?? new SizeRow(kind, key, path);

    // Brings the list to the wanted rows by moving, inserting and removing,
    // never by replacing the whole list, so a running analysis does not reset
    // the scroll position or the person's selection.
    private void Replace(List<SizeRow> wanted)
    {
        SizeRow? selected = Rows.SelectedItem as SizeRow;
        _filling = true;
        try
        {
            var keep = new HashSet<SizeRow>(wanted);
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (!keep.Contains(_rows[i])) _rows.RemoveAt(i);
            for (int i = 0; i < wanted.Count; i++)
            {
                int at = _rows.IndexOf(wanted[i]);
                if (at < 0) _rows.Insert(i, wanted[i]);
                else if (at != i) _rows.Move(at, i);
            }
            if (selected is not null && keep.Contains(selected) && !ReferenceEquals(Rows.SelectedItem, selected))
                Rows.SelectedItem = selected;
        }
        finally
        {
            _filling = false;
        }
    }

    // Shows the selection the window keeps: a folder's row, or one of the
    // heaviest files under its folder's row. Anything else, a file the file
    // tool selected for instance, stays the window's selection without a row.
    private void ShowSelection(SizeReport report, string? selection)
    {
        SizeRow? row = null;
        if (selection is not null)
        {
            HeavyFile? heavy = report.Heaviest.FirstOrDefault(f => FolderPaths.Comparer.Equals(f.Path, selection));
            if (heavy is not null && _heavy?.Path != heavy.Path)
            {
                _heavy = heavy;
                ShowRows(report);
            }
            row = _rows.FirstOrDefault(r => r.Path is not null && FolderPaths.Comparer.Equals(r.Path, selection));
        }

        _filling = true;
        try
        {
            Rows.SelectedItem = row;
            if (row is not null) Rows.ScrollIntoView(row);
        }
        finally
        {
            _filling = false;
        }
        _panel.Mark(row?.Kind == SizeRowKind.HeavyFile ? row.Path : null);
        _host.Select(selection, _panel);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        var row = Rows.SelectedItem as SizeRow;
        _wanted = row?.Path;

        // The picked heavy file's row says where that file is; once the
        // person selects another row, it has nothing more to say.
        if (_heavy is not null && row?.Kind != SizeRowKind.HeavyFile)
        {
            _heavy = null;
            if (_report is not null) ShowRows(_report);
        }
        _panel.Mark(row?.Kind == SizeRowKind.HeavyFile ? row.Path : null);
        _host.Select(row?.Path, _panel);
    }

    // A click on one of the heaviest files in the panel selects it in the main
    // list, under the row of its folder, which shows where it is (12.21).
    private void OnHeavyPicked(HeavyFile file)
    {
        if (_report is null) return;
        _heavy = file;
        _wanted = file.Path;
        ShowRows(_report);
        SizeRow? row = _rows.FirstOrDefault(r => r.Kind == SizeRowKind.HeavyFile);
        _filling = true;
        try
        {
            Rows.SelectedItem = row;
            if (row is not null) Rows.ScrollIntoView(row);
        }
        finally
        {
            _filling = false;
        }
        _host.Select(file.Path, _panel);
    }

    private void OnRowsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SizeRow row) Enter(row);
    }

    private void OnRowsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Rows.SelectedItem is SizeRow row)
        {
            Enter(row);
            e.Handled = true;
        }
    }

    // Goes into a folder through the window, as the file tool does; the report
    // in hand is re-rooted there when it holds that folder (ShowAsync).
    private void Enter(SizeRow row)
    {
        if (row.Kind != SizeRowKind.Folder || row.Path is null || _place is null) return;
        _host.Open(_place.Enter(row.Path));
    }

    // Stops the analysis and keeps what it measured (12.23); Refresh runs it
    // again.
    private void OnStopClick(object sender, RoutedEventArgs e) => _scan?.Cancel();
}
