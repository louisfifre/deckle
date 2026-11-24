using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Deckle.Core;

namespace Deckle.Folders.Views;

// Where the window stands, Home or a folder, and Back, Forward, Up and Refresh.
public sealed partial class FoldersWindow
{
    // Home and folders in one history, so Back and Forward cross both as File
    // Explorer's do (boards 24/11, A2).
    private readonly NavigationHistory<Destination> _history = new();

    // The folder the window stands in; null at Home and while the history has
    // forgotten where it stood.
    private FolderPlace? Place => (_history.Current as FolderDestination)?.Place;

    private bool AtHome => _history.Current is HomeDestination;

    // Going to a new folder starts with nothing selected and no panel (12.20);
    // the selection stays only while the folder stays.
    private Task NavigateTo(FolderPlace place)
    {
        if (!_history.Navigate(new FolderDestination(place))) return Task.CompletedTask;
        _selection = null;
        return ShowPlaceAsync();
    }

    private void NavigateHome()
    {
        if (!_history.Navigate(HomeDestination.Instance)) return;
        _selection = null;
        ShowHome();
    }

    // What Back and Forward arrive at: Home or a folder.
    private void ShowDestination()
    {
        _selection = null;
        if (AtHome) ShowHome();
        else _ = ShowPlaceAsync();
    }

    private int _placeRequest;

    // Before the tool, the added folder itself is asked whether it is still
    // there: one that vanished or cannot be read shows its message in place
    // of the tool (FoldersWindow.Folders.cs), and no tool runs on it.
    private async Task ShowPlaceAsync()
    {
        FolderPlace place = Place!;
        int request = ++_placeRequest;
        // What a tool was still showing belongs to the place left.
        _showing?.Cancel();
        _unreachableShown = null;
        EmptyState.Visibility = Visibility.Collapsed;
        CentreHost.Visibility = Visibility.Visible;
        MarkActiveTool();
        SetTopRowEnabled(true);
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
        UpButton.IsEnabled = place.Parent is not null;
        SelectFolderItem(place.Root);
        _ = Address.ShowAsync(place);

        AddedFolderReachability reach = await Task.Run(() => AddedFolderReach.Check(place.Root.Path));
        if (request != _placeRequest) return;
        NoteReach(place.Root.Path, reach);
        if (reach != AddedFolderReachability.Reachable)
        {
            ShowUnreachable(place.Root, reach);
            return;
        }
        _showingFolder = true;
        await ShowCurrentToolAsync();
    }

    // Home: one tile per added folder (HomeView), no tool and no panel, so
    // the three tool toggles and the details toggle are disabled and none is
    // pressed; Up has nowhere to go, Back and Forward follow the history, and
    // Refresh asks the disk about every added folder again. With no folder
    // added the empty state shows instead, and every button of the top row is
    // disabled except the pane's (12.11).
    private void ShowHome()
    {
        ++_placeRequest;
        _showing?.Cancel();
        _unreachableShown = null;
        _showingFolder = false;
        Select(null, null);
        MarkActiveTool();
        SetTopRowEnabled(false);
        SelectHomeItem();
        Address.ShowHome();

        if (_folders.Folders.Count == 0)
        {
            CentreHost.Content = null;
            CentreHost.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        CentreHost.Visibility = Visibility.Visible;
        // Built again from the store each time, so a folder added, removed or
        // given a new icon shows.
        Home.Show(_folders.Folders);
        CentreHost.Content = Home;
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
        RefreshButton.IsEnabled = true;
        SearchButton.IsEnabled = true;
    }

    private void SetTopRowEnabled(bool enabled)
    {
        BackButton.IsEnabled = enabled;
        ForwardButton.IsEnabled = enabled;
        UpButton.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        SearchButton.IsEnabled = enabled;
        DetailsToggle.IsEnabled = enabled;
        foreach (UIElement button in ToolButtons.Children)
            if (button is Microsoft.UI.Xaml.Controls.Control control) control.IsEnabled = enabled;
    }

    private void GoBack()
    {
        if (_history.GoBack() is null) return;
        ShowDestination();
    }

    private void GoForward()
    {
        if (_history.GoForward() is null) return;
        ShowDestination();
    }

    private void GoUp()
    {
        if (Place?.Parent is { } parent) _ = NavigateTo(parent);
    }

    // Re-runs the current tool on the current folder (spec, Open 17). It
    // asks the disk again about every added folder too, so one that came
    // back, its drive plugged in again, loses its mark and opens. At Home it
    // shows Home again.
    private void Refresh()
    {
        if (AtHome)
        {
            _reachChecked = CheckFolderReachAsync();
            ShowHome();
            return;
        }
        if (Place is null) return;
        _reachChecked = CheckFolderReachAsync();
        _ = ShowPlaceAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => GoBack();
    private void OnForwardClick(object sender, RoutedEventArgs e) => GoForward();
    private void OnUpClick(object sender, RoutedEventArgs e) => GoUp();
    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    // The address bar's text field keeps its own Alt+arrow and F5 keys.
    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Refresh();
        args.Handled = true;
    }

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Address.IsEditing) return;
        GoBack();
        args.Handled = true;
    }

    private void OnForwardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Address.IsEditing) return;
        GoForward();
        args.Handled = true;
    }

    private void OnUpAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Address.IsEditing) return;
        GoUp();
        args.Handled = true;
    }

    // What the self-check drives: Refresh as F5 runs it, awaited, with the
    // pane's marks settled; where the window stands; whether the address
    // bar cuts a segment's name.
    public async Task RefreshForReviewAsync()
    {
        if (AtHome)
        {
            Refresh();
            await _reachChecked;
            return;
        }
        if (Place is null) return;
        _reachChecked = CheckFolderReachAsync();
        await ShowPlaceAsync();
        await _reachChecked;
    }

    public string? CurrentFolderForReview => Place?.Root.Path;

    public bool CanGoBackForReview => _history.CanGoBack;

    public bool CanGoForwardForReview => _history.CanGoForward;

    public bool AtHomeForReview => AtHome;

    // A tile of the Home page, as a click on it opens it.
    public bool OpenHomeTileForReview(string path)
    {
        if (_folders.Find(path) is not { } folder) return false;
        OpenHomeTile(folder);
        return true;
    }

    public void GoBackForReview() => GoBack();

    public void NavigateHomeForReview() => NavigateHome();

    public bool AddressCutsASegmentForReview => Address.HasTrimmedSegment;

    private void OnAddressAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Address.EditAddress();
        args.Handled = true;
    }
}
