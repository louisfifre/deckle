using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// The two actions of one row of the file tool, in its three displays. They
// show while the pointer is over the row, or while the keyboard's focus is on
// the row or on one of them, and are otherwise transparent in their place:
// the room they take never changes (spec, File list).
//
// The row is the display's container (a ListViewItem or a GridViewItem),
// found above the control once it is in the tree; its hover and focus are
// heard there, so the whole row reveals the actions, not only their corner.
// A pointer focus does not count: a row clicked and then left by the pointer
// hides them again, as a keyboard focus would not.
//
// Each button says its action in a tooltip, and its accessible name adds the
// item's name, so a screen reader says which row it acts on.
internal sealed partial class FileRowActions : UserControl
{
    // Two 32-pixel buttons and the 4 pixels between them.
    public const double ReservedWidth = 68;

    private SelectorItem? _row;
    private bool _pointerOver;
    private bool _keyboardFocus;

    public FileRowActions()
    {
        InitializeComponent();
        Width = ReservedWidth;
        ShowInFolderIcon.Glyph = FoldersGlyphs.ShowInFolder;
        RecycleIcon.Glyph = FoldersGlyphs.RecycleBin;
        ToolTipService.SetToolTip(ShowInFolderButton, FoldersText.Get("Files_ShowInFolder"));
        ToolTipService.SetToolTip(RecycleButton, FoldersText.Get("Files_Recycle"));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    // Raised with this control as the sender; its DataContext is the row's FileRow.
    public event RoutedEventHandler? ShowInFolderClick;
    public event RoutedEventHandler? RecycleClick;

    // Over a picture (the gallery's tiles): the frame that keeps the icons
    // readable whatever is under them.
    public bool OverContent
    {
        get => Frame.Background is not null;
        set
        {
            Frame.Background = value ? Resource<Brush>("AcrylicInAppFillColorDefaultBrush") : null;
            Frame.BorderBrush = value ? Resource<Brush>("CardStrokeColorDefaultBrush") : null;
            Frame.BorderThickness = new Thickness(value ? 1 : 0);
        }
    }

    // The pointer came over the row or left it; the self-check takes this
    // path to photograph a row with its actions.
    internal void SetPointerOver(bool over)
    {
        _pointerOver = over;
        Show();
    }

    private void Show() => Opacity = _pointerOver || _keyboardFocus ? 1 : 0;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SelectorItem? row = RowOf(this);
        if (ReferenceEquals(row, _row)) return;
        Detach();
        _row = row;
        if (row is null) return;
        row.PointerEntered += OnRowPointerEntered;
        row.PointerExited += OnRowPointerLeft;
        row.PointerCanceled += OnRowPointerLeft;
        row.GotFocus += OnRowGotFocus;
        row.LostFocus += OnRowLostFocus;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
        _pointerOver = false;
        _keyboardFocus = false;
        Show();
    }

    private void Detach()
    {
        if (_row is not { } row) return;
        row.PointerEntered -= OnRowPointerEntered;
        row.PointerExited -= OnRowPointerLeft;
        row.PointerCanceled -= OnRowPointerLeft;
        row.GotFocus -= OnRowGotFocus;
        row.LostFocus -= OnRowLostFocus;
        _row = null;
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(true);

    // Pressing a button captures the pointer, which is still over the row: a
    // lost capture is not an exit, and is not heard. Exited still comes when
    // the pointer leaves.
    private void OnRowPointerLeft(object sender, PointerRoutedEventArgs e) => SetPointerOver(false);

    private void OnRowGotFocus(object sender, RoutedEventArgs e)
    {
        _keyboardFocus = e.OriginalSource is UIElement { FocusState: FocusState.Keyboard };
        Show();
    }

    // Focus moving from the row to one of its buttons stays inside the row.
    private void OnRowLostFocus(object sender, RoutedEventArgs e)
    {
        if (_row is { XamlRoot: { } root } row
            && FocusManager.GetFocusedElement(root) is DependencyObject focused
            && IsInside(focused, row))
            return;
        _keyboardFocus = false;
        Show();
    }

    // A recycled container shows another item: its names follow, and what it
    // showed for the last one is dropped.
    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        string name = (args.NewValue as FileRow)?.Name ?? "";
        AutomationProperties.SetName(ShowInFolderButton, FoldersText.Format("Files_ShowInFolder_Name_Format", name));
        AutomationProperties.SetName(RecycleButton, FoldersText.Format("Files_Recycle_Name_Format", name));
    }

    private void OnShowInFolderClick(object sender, RoutedEventArgs e) => ShowInFolderClick?.Invoke(this, e);

    private void OnRecycleClick(object sender, RoutedEventArgs e) => RecycleClick?.Invoke(this, e);

    private static SelectorItem? RowOf(DependencyObject element)
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is SelectorItem item) return item;
        }
        return null;
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, ancestor)) return true;
        }
        return false;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
