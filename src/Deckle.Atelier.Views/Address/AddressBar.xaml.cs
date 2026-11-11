using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Deckle.Atelier.Views;

// The address bar's behaviour. It shows a place and asks the window to go
// somewhere; the window owns the place and the history.
internal sealed partial class AddressBar : UserControl
{
    private FolderPlace? _place;
    private bool _dialogOpen;
    private int _suggestionRequest;

    public AddressBar()
    {
        InitializeComponent();
        Placeholder.Text = AtelierText.Get("Address_None");
        AutomationProperties.SetName(PathBox, AtelierText.Get("Address_Label"));
    }

    public AddedFolderStore? Folders { get; set; }

    public event Action<FolderPlace>? NavigateRequested;

    public bool IsEditing => PathBox.Visibility == Visibility.Visible;

    // No folder added yet (12.11): the bar says so and takes no click.
    public void ShowNone()
    {
        _place = null;
        LeaveTextMode();
        Crumbs.ItemsSource = null;
        Crumbs.Visibility = Visibility.Collapsed;
        RootIcon.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
    }

    public async Task ShowAsync(FolderPlace place)
    {
        _place = place;
        IReadOnlyList<PathSegment> segments = place.Segments;
        // Whether each segment has folders inside decides its chevron; that
        // reads the disk, so off the UI thread.
        bool[] hasSubfolders = await Task.Run(() => segments.Select(s => FolderListing.Subfolders(s.Path).Count > 0).ToArray());
        if (!ReferenceEquals(_place, place)) return;

        Crumbs.ItemsSource = segments
            .Select((s, i) => new AddressCrumb(s, new FolderPlace(place.Root, s.Path), hasSubfolders[i]))
            .ToList();
        Crumbs.Visibility = Visibility.Visible;
        RootIcon.Visibility = Visibility.Visible;
        Placeholder.Visibility = Visibility.Collapsed;
    }

    // Ctrl+L, Alt+D: straight to the text mode, as in File Explorer.
    public void EditAddress()
    {
        if (_place is not null) EnterTextMode();
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
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (FolderEntry folder in FolderListing.Subfolders(crumb.Place.Path))
        {
            bool current = onPath is not null && FolderPaths.Comparer.Equals(onPath.Path, folder.Path);
            var item = new MenuFlyoutItem
            {
                Text = folder.Name,
                Icon = new FontIcon { Glyph = AtelierGlyphs.Folder },
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
            };
            FolderPlace target = crumb.Place.Enter(folder.Path);
            item.Click += (_, _) => NavigateRequested?.Invoke(target);
            flyout.Items.Add(item);
        }
        if (flyout.Items.Count > 0) flyout.ShowAt(button);
    }

    // Only the empty part of the bar turns it into a text field; a segment or
    // a chevron keeps its own click.
    private void OnSegmentsModeTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SegmentsMode) && _place is not null) EnterTextMode();
    }

    private void EnterTextMode()
    {
        PathBox.Text = _place!.Path;
        PathBox.ItemsSource = null;
        SegmentsMode.Visibility = Visibility.Collapsed;
        PathBox.Visibility = Visibility.Visible;
        PathBox.Focus(FocusState.Programmatic);
        // File Explorer selects the whole path, so typing replaces it.
        DispatcherQueue.TryEnqueue(() => FindDescendant<TextBox>(PathBox)?.SelectAll());
    }

    private void LeaveTextMode()
    {
        PathBox.Visibility = Visibility.Collapsed;
        SegmentsMode.Visibility = Visibility.Visible;
    }

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
                Content = AtelierText.Format(
                    resolution.Failure == AddressFailure.OutsideAddedFolders ? "Address_Outside_Format" : "Address_NotFound_Format",
                    text.Trim()),
                CloseButtonText = AtelierText.Get("Common_Ok"),
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

    private void OnPathLostFocus(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        // Focus moving into the suggestion list stays inside the box.
        if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsInside(focused, PathBox)) return;
        LeaveTextMode();
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
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
