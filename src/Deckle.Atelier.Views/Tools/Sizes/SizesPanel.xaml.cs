using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// One of the heaviest files beneath the place, as the panel lists it: its name,
// the folder it is in written from the place (empty when it lies in the place
// itself, so the path never only repeats the name: spec, Sizes), and its size.
internal sealed class HeavyItem(HeavyFile file, string place)
{
    public HeavyFile File { get; } = file;
    public string Name => File.Name;
    public string Where { get; } = file.FolderRelativeTo(place);
    public Visibility WhereVisibility => Where.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public string Size { get; } = EntryFormat.Size(file.Size, AtelierText.Culture);

    public override string ToString() =>
        Where.Length == 0
            ? AtelierText.Format("Sizes_Row_Automation_Format", Name, Size)
            : AtelierText.Format("Sizes_Heavy_Automation_Format", Name, Where, Size);
}

// The panel of the sizes tool: the folder the person is in, its total, how the
// analysis went, and its heaviest files (12.7). It stays about the folder when a
// heavy file is selected (spec, Open 24: as drawn in 12.21); the file picked is
// only marked in its list.
internal sealed partial class SizesPanel : UserControl
{
    private IReadOnlyList<HeavyFile> _shown = [];
    private bool _marking;

    public SizesPanel()
    {
        InitializeComponent();
        HeaviestTitle.Text = AtelierText.Get("Sizes_Heaviest");
        HeaviestNone.Text = AtelierText.Get("Sizes_Heaviest_None");
        AutomationProperties.SetName(Heaviest, HeaviestTitle.Text);
    }

    // A heavy file was picked in the list, by a click or the keyboard.
    public event Action<HeavyFile>? HeavyPicked;

    public void Show(SizeReport report, string folderName)
    {
        string total = report.IsComplete || report.State == SizeScanState.Cancelled
            ? EntryFormat.Size(report.TotalBytes, AtelierText.Culture)
            : AtelierText.Get("Sizes_Pending");
        string? state = report.State switch
        {
            SizeScanState.Running => AtelierText.Get("Sizes_Panel_Running"),
            SizeScanState.Cancelled => AtelierText.Get("Sizes_Stopped"),
            SizeScanState.Failed => AtelierText.Get("Sizes_Panel_Failed"),
            _ => null,
        };
        string? unreadable = report.UnreadableFolders switch
        {
            0 => null,
            1 => AtelierText.Get("Sizes_Panel_Unreadable_One"),
            long n => AtelierText.Format("Sizes_Panel_Unreadable_Many_Format", n.ToString("N0", AtelierText.Culture)),
        };

        SheetHost.Content = new DetailsSheet(AtelierText.Get("Sizes_Panel_Kind"), folderName)
            .Section(
                AtelierText.Get("Sizes_Panel_Section"),
                (AtelierText.Get("Sizes_Panel_Total"), total),
                (AtelierText.Get("Sizes_Panel_State"), state),
                (AtelierText.Get("Sizes_Panel_Unreadable"), unreadable))
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

    private void OnHeaviestSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_marking) return;
        if (Heaviest.SelectedItem is HeavyItem item) HeavyPicked?.Invoke(item.File);
    }
}
