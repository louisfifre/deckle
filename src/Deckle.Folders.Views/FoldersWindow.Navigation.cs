using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Deckle.Core;

namespace Deckle.Folders.Views;

// Where the window stands, and Back, Forward, Up and Refresh.
public sealed partial class FoldersWindow
{
    private readonly NavigationHistory<FolderPlace> _history = new();

    private FolderPlace? Place => _history.Current;

    // Going to a new folder starts with nothing selected and no panel (12.20);
    // the selection stays only while the folder stays.
    private Task NavigateTo(FolderPlace place)
    {
        if (!_history.Navigate(place)) return Task.CompletedTask;
        _selection = null;
        return ShowPlaceAsync();
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
        await ShowCurrentToolAsync();
    }

    // With no folder added, every button of the top row is disabled except
    // the pane's, and the address bar says "No folder" (12.11).
    private void ShowEmptyState()
    {
        ++_placeRequest;
        _showing?.Cancel();
        _unreachableShown = null;
        CentreHost.Content = null;
        CentreHost.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        Select(null, null);
        SetTopRowEnabled(false);
        Address.ShowNone();
    }

    private void SetTopRowEnabled(bool enabled)
    {
        BackButton.IsEnabled = enabled;
        ForwardButton.IsEnabled = enabled;
        UpButton.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        SearchButton.IsEnabled = enabled;
        foreach (UIElement button in ToolButtons.Children)
            if (button is Microsoft.UI.Xaml.Controls.Control control) control.IsEnabled = enabled;
    }

    private void GoBack()
    {
        if (_history.GoBack() is null) return;
        _selection = null;
        _ = ShowPlaceAsync();
    }

    private void GoForward()
    {
        if (_history.GoForward() is null) return;
        _selection = null;
        _ = ShowPlaceAsync();
    }

    private void GoUp()
    {
        if (Place?.Parent is { } parent) _ = NavigateTo(parent);
    }

    // Re-runs the current tool on the current folder (spec, Open 17). It
    // asks the disk again about every added folder too, so one that came
    // back, its drive plugged in again, loses its mark and opens.
    private void Refresh()
    {
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
        if (Place is null) return;
        _reachChecked = CheckFolderReachAsync();
        await ShowPlaceAsync();
        await _reachChecked;
    }

    public string? CurrentFolderForReview => Place?.Root.Path;

    public bool CanGoBackForReview => _history.CanGoBack;

    public bool AddressCutsASegmentForReview => Address.HasTrimmedSegment;

    private void OnAddressAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Address.EditAddress();
        args.Handled = true;
    }
}
