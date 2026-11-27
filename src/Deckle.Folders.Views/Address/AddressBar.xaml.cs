using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Deckle.Core;

namespace Deckle.Folders.Views;

// The address bar's behaviour. It shows a place and asks the window to go
// somewhere; the window owns the place and the history.
internal sealed partial class AddressBar : UserControl
{
    // The field's forms (boards 24/11, C1, F1). Short is the rest state;
    // Home has its own look and only ever shows Short or Text.
    private enum AddressMode { Short, Full, Text }

    private FolderPlace? _place;
    private AddressShortForm? _short;
    private AddressMode _mode = AddressMode.Short;
    private bool _atHome;
    private bool _showsCurrentOnly;
    private bool _dialogOpen;
    private bool _chevronListOpen;
    private int _suggestionRequest;

    public AddressBar()
    {
        InitializeComponent();
        HomeLabel.Text = FoldersText.Get("Nav_Home");
        AutomationProperties.SetName(PathBox, FoldersText.Get("Address_Label"));
    }

    public AddedFolderStore? Folders { get; set; }

    public event Action<FolderPlace>? NavigateRequested;

    public bool IsEditing => _mode == AddressMode.Text;

    public bool IsFull => _mode == AddressMode.Full;

    public static readonly DependencyProperty TrailingContentProperty = DependencyProperty.Register(
        nameof(TrailingContent), typeof(UIElement), typeof(AddressBar),
        new PropertyMetadata(null, (d, e) => ((AddressBar)d).TrailingHost.Content = e.NewValue));

    // What sits at the field's right end in every mode: the search button, kept
    // by the window with its flyout and its handlers.
    public UIElement? TrailingContent
    {
        get => (UIElement?)GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    // The compact form for narrow windows: at rest the field keeps the icon
    // and the current name, with no drive, no ellipsis and no separator.
    public bool ShowsCurrentOnly
    {
        get => _showsCurrentOnly;
        set
        {
            if (_showsCurrentOnly == value) return;
            _showsCurrentOnly = value;
            ApplyMode();
        }
    }

    // Home (boards 24/11, A2): the Home glyph in the icon's slot, with
    // nothing to click, and one segment, "Home", with no chevron and no click.
    // A click on the bar's empty part, or Ctrl+L, still opens the text field,
    // empty, to type a path from nowhere.
    public void ShowHome()
    {
        _place = null;
        _short = null;
        _atHome = true;
        Crumbs.ItemsSource = null;
        SetMode(AddressMode.Short);
    }

    public async Task ShowAsync(FolderPlace place)
    {
        _place = place;
        _atHome = false;
        _short = AddressShortForm.Of(place);
        DriveLabel.Text = _short.DriveLabel ?? "";
        CurrentName.Text = _short.CurrentName;
        ShowIcon(place.Root);
        // A new place always shows at rest, whatever form the last one was in.
        SetMode(AddressMode.Short);
        IReadOnlyList<PathSegment> segments = place.Segments;
        // Whether each segment has folders inside decides its chevron; that
        // reads the disk, so off the UI thread.
        bool[] hasSubfolders = await Task.Run(() => segments.Select(s => FolderListing.Subfolders(s.Path).Count > 0).ToArray());
        if (!ReferenceEquals(_place, place)) return;

        Crumbs.ItemsSource = segments
            .Select((s, i) => new AddressCrumb(s, new FolderPlace(place.Root, s.Path), hasSubfolders[i]))
            .ToList();
        FitSegmentNames();
    }

    // Which parts of the field show in each form. One place decides it, so no
    // form leaves a part of another behind.
    private void SetMode(AddressMode mode)
    {
        _mode = mode;
        bool text = mode == AddressMode.Text;
        PathBox.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
        SegmentsMode.Visibility = text ? Visibility.Collapsed : Visibility.Visible;
        ApplyMode();
    }

    private void ApplyMode()
    {
        bool folder = !_atHome && _short is not null;
        bool full = _mode == AddressMode.Full;
        bool compact = _showsCurrentOnly && !full;
        IconButton.Visibility = Show(folder);
        HomeIcon.Visibility = Show(_atHome);
        HomeLabel.Visibility = Show(_atHome);
        DrivePrefix.Visibility = Show(folder && _short!.DriveLabel is not null && !compact);
        Crumbs.Visibility = Show(folder && full);
        ShortTail.Visibility = Show(folder && !full);
        bool hides = folder && _short!.HidesSegments && !compact;
        HiddenSegments.Visibility = Show(hides);
        HiddenSeparator.Visibility = Show(hides);
        // Keyboard focus rests on the field in its full form, so the field
        // hears it leave; at rest it is no tab stop of its own.
        IsTabStop = full;
        FitSegmentNames();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // Ctrl+L, Alt+D: straight to the text mode, as in File Explorer.
    public void EditAddress()
    {
        if (_place is not null || _atHome) EnterTextMode();
    }

    // The click that opens the full form, as a person makes it.
    public void OpenFull()
    {
        if (_place is null || _mode == AddressMode.Text) return;
        SetMode(AddressMode.Full);
        Focus(FocusState.Programmatic);
    }

    // What the self-check drives: the list a chevron opens, for the segment at
    // this index of the path. False when that chevron is not there.
    public bool OpenChevronListForReview(int segmentIndex)
    {
        if (_place is null || segmentIndex >= _place.Segments.Count) return false;
        string path = _place.Segments[segmentIndex].Path;
        Button? button = FindChevron(Crumbs, path);
        if (button is null) return false;
        OnChevronClick(button, new RoutedEventArgs());
        return _chevronListOpen;
    }

    private static Button? FindChevron(DependencyObject root, string path)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Button { Tag: AddressCrumb crumb } b && FolderPaths.Comparer.Equals(crumb.Place.Path, path)) return b;
            if (FindChevron(child, path) is { } found) return found;
        }
        return null;
    }

    private void OnCrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is AddressCrumb crumb && !crumb.Place.Equals(_place))
            NavigateRequested?.Invoke(crumb.Place);
    }

    // The list of folders at that level, the one on the current path marked
    // (12.12a). A choice goes there at once.
    private void OnChevronClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AddressCrumb crumb } button || _place is null) return;

        PathSegment? onPath = _place.Segments.FirstOrDefault(s =>
            FolderPaths.Comparer.Equals(System.IO.Path.GetDirectoryName(s.Path), crumb.Place.Path));
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, ShouldConstrainToRootBounds = true };
        foreach (FolderEntry folder in FolderListing.Subfolders(crumb.Place.Path))
        {
            bool current = onPath is not null && FolderPaths.Comparer.Equals(onPath.Path, folder.Path);
            var item = new MenuFlyoutItem
            {
                Text = folder.Name,
                Icon = new FontIcon { Glyph = FoldersGlyphs.Folder },
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
            };
            FolderPlace target = crumb.Place.Enter(folder.Path);
            item.Click += (_, _) => NavigateRequested?.Invoke(target);
            flyout.Items.Add(item);
        }
        if (flyout.Items.Count == 0) return;
        // The list takes keyboard focus out of the field, which must not read
        // as the person leaving it.
        flyout.Closed += (_, _) => _chevronListOpen = false;
        _chevronListOpen = true;
        flyout.ShowAt(button);
    }

    // At rest, a click anywhere but the icon opens the full form. In the full
    // form (and at Home) only the empty part turns the field into a text
    // field; a segment or a chevron keeps its own click.
    private void OnSegmentsModeTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_place is null && !_atHome) return;
        var source = e.OriginalSource as DependencyObject;
        if (_atHome)
        {
            if (!ReferenceEquals(source, SegmentsMode) && !IsInside(source, HomeLabel) && !IsInside(source, HomeIcon)) return;
            EnterTextMode();
        }
        else if (_mode == AddressMode.Short)
        {
            if (IsInside(source, IconButton)) return;
            OpenFull();
        }
        else if (_mode == AddressMode.Full && (ReferenceEquals(source, SegmentsMode) || IsInside(source, DrivePrefix)))
        {
            EnterTextMode();
        }
    }

    // Keyboard focus leaving the field in its full form brings it back to rest.
    private void OnFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if (_mode != AddressMode.Full || _chevronListOpen) return;
        if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsInside(focused, this)) return;
        SetMode(AddressMode.Short);
    }

    private void OnCurrentNameTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args) =>
        ToolTipService.SetToolTip(sender, sender.IsTextTrimmed ? sender.Text : null);

    private void EnterTextMode()
    {
        // From Home there is no path to start from.
        PathBox.Text = _place?.Path ?? "";
        PathBox.ItemsSource = null;
        SetMode(AddressMode.Text);
        PathBox.Focus(FocusState.Programmatic);
        // File Explorer selects the whole path, so typing replaces it.
        DispatcherQueue.TryEnqueue(() => FindDescendant<TextBox>(PathBox)?.SelectAll());
    }

    // Back to rest, from the text field or the full form.
    private void LeaveTextMode() => SetMode(AddressMode.Short);

    private async void OnPathTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || Folders is not { } folders) return;
        int request = ++_suggestionRequest;
        string text = sender.Text;
        IReadOnlyList<string> suggestions = await Task.Run(() => AddressResolver.Suggestions(text, folders));
        if (request == _suggestionRequest) sender.ItemsSource = suggestions;
    }

    private async void OnPathSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (Folders is not { } folders) return;
        string text = args.ChosenSuggestion as string ?? args.QueryText;
        AddressResolution resolution = AddressResolver.Resolve(text, folders);
        if (resolution.Place is { } place)
        {
            LeaveTextMode();
            if (!place.Equals(_place)) NavigateRequested?.Invoke(place);
            return;
        }
        if (resolution.Failure == AddressFailure.Empty)
        {
            LeaveTextMode();
            return;
        }

        // File Explorer answers an address it cannot reach with a dialog and
        // keeps the text, so the person corrects it.
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Deckle Folders",
                Content = FoldersText.Format(
                    resolution.Failure == AddressFailure.OutsideAddedFolders ? "Address_Outside_Format" : "Address_NotFound_Format",
                    text.Trim()),
                CloseButtonText = FoldersText.Get("Common_Ok"),
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
        PathBox.Focus(FocusState.Programmatic);
    }

    private void OnPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        LeaveTextMode();
        e.Handled = true;
    }

    // Escape in the full form returns to rest as well.
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != VirtualKey.Escape || _mode != AddressMode.Full) return;
        SetMode(AddressMode.Short);
        e.Handled = true;
    }

    private void OnPathLostFocus(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        // Focus moving into the suggestion list stays inside the box.
        if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsInside(focused, PathBox)) return;
        LeaveTextMode();
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, container)) return true;
        return false;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
