using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Core;

namespace Deckle.Folders.Views;

// One of the heaviest files beneath the place, as the panel lists it: its name,
// the folder it is in written from the place (empty when it lies in the place
// itself, so the path never only repeats the name: spec, Sizes), and its size.
//
// Its icon is the one Windows gives the file, asked for when the list realizes
// the item. There is no glyph to fall back on: the cell stays empty until the
// image comes, and when the shell has none.
internal sealed class HeavyItem(HeavyFile file, string place) : System.ComponentModel.INotifyPropertyChanged
{
    private ImageSource? _icon;
    private CancellationTokenSource? _loading;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public async Task LoadIconAsync(IFileIconProvider icons, int pixelSize)
    {
        if (_icon is not null) return;
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        try
        {
            Icon = await icons.GetAsync(File.Path, isFolder: false, pixelSize, loading.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void ReleaseIcon()
    {
        _loading?.Cancel();
        _loading = null;
    }

    public HeavyFile File { get; } = file;
    public string Name => File.Name;
    public string Where { get; } = file.FolderRelativeTo(place);
    public Visibility WhereVisibility => Where.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public string Size { get; } = EntryFormat.Size(file.Size, FoldersText.Culture);

    public override string ToString() =>
        Where.Length == 0
            ? FoldersText.Format("Sizes_Row_Automation_Format", Name, Size)
            : FoldersText.Format("Sizes_Heavy_Automation_Format", Name, Where, Size);
}

// The panel of the sizes tool: the folder the person is in, its total, how the
// analysis went, and its heaviest files (12.7). It stays about the folder when a
// heavy file is selected (spec, Open 24: as drawn in 12.21); the file picked is
// only marked in its list.
internal sealed partial class SizesPanel : UserControl
{
    private IReadOnlyList<HeavyFile> _shown = [];
    private bool _marking;
    private readonly IFileIconProvider _icons;

    public SizesPanel(IFileIconProvider icons)
    {
        _icons = icons;
        InitializeComponent();
        HeaviestTitle.Text = FoldersText.Get("Sizes_Heaviest");
        HeaviestNone.Text = FoldersText.Get("Sizes_Heaviest_None");
        AutomationProperties.SetName(Heaviest, HeaviestTitle.Text);
    }

    // A heavy file was picked in the list, by a click or the keyboard.
    public event Action<HeavyFile>? HeavyPicked;

    public void Show(SizeReport report, string folderName)
    {
        string total = report.IsComplete || report.State == SizeScanState.Cancelled
            ? EntryFormat.Size(report.TotalBytes, FoldersText.Culture)
            : FoldersText.Get("Sizes_Pending");
        string? state = report.State switch
        {
            SizeScanState.Running => FoldersText.Get("Sizes_Panel_Running"),
            SizeScanState.Cancelled => FoldersText.Get("Sizes_Stopped"),
            SizeScanState.Failed => FoldersText.Get("Sizes_Panel_Failed"),
            _ => null,
        };
        string? unreadable = report.UnreadableFolders switch
        {
            0 => null,
            1 => FoldersText.Get("Sizes_Panel_Unreadable_One"),
            long n => FoldersText.Format("Sizes_Panel_Unreadable_Many_Format", n.ToString("N0", FoldersText.Culture)),
        };

        SheetHost.Content = new DetailsSheet(FoldersText.Get("Sizes_Panel_Kind"), folderName)
            .Section(
                FoldersText.Get("Sizes_Panel_Section"),
                (FoldersText.Get("Sizes_Panel_Total"), total),
                (FoldersText.Get("Sizes_Panel_State"), state),
                (FoldersText.Get("Sizes_Panel_Unreadable"), unreadable))
            .Element;

        // The six files change while the analysis runs; the list is rebuilt
        // only when they do, so a tick never resets it for nothing.
        if (!_shown.SequenceEqual(report.Heaviest))
        {
            _shown = report.Heaviest;
            _marking = true;
            try
            {
                Heaviest.ItemsSource = report.Heaviest.Select(f => new HeavyItem(f, report.Place)).ToList();
            }
            finally
            {
                _marking = false;
            }
        }
        bool none = report.Heaviest.Count == 0 && report.State != SizeScanState.Running;
        HeaviestNone.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        Heaviest.Visibility = report.Heaviest.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // Marks the file selected in the main list, or none, without taking it for
    // a pick of the person's.
    public void Mark(string? path)
    {
        _marking = true;
        try
        {
            Heaviest.SelectedItem = path is null
                ? null
                : (Heaviest.ItemsSource as List<HeavyItem>)?.FirstOrDefault(i => FolderPaths.Comparer.Equals(i.File.Path, path));
        }
        finally
        {
            _marking = false;
        }
    }

    private void OnHeaviestContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not HeavyItem item) return;
        if (args.InRecycleQueue)
        {
            item.ReleaseIcon();
            return;
        }
        // The same 16 DIP cell as a row of the sizes list.
        _ = item.LoadIconAsync(_icons, (int)Math.Round(16 * (XamlRoot?.RasterizationScale ?? 1.0)));
    }

    private void OnHeaviestSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_marking) return;
        if (Heaviest.SelectedItem is HeavyItem item) HeavyPicked?.Invoke(item.File);
    }
}
