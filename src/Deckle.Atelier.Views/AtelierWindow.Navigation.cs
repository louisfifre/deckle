using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Atelier.Views;

// Where the window stands, and Back, Forward, Up and Refresh.
public sealed partial class AtelierWindow
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

    private Task ShowPlaceAsync()
    {
        FolderPlace place = Place!;
        EmptyState.Visibility = Visibility.Collapsed;
        CentreHost.Visibility = Visibility.Visible;
        SetTopRowEnabled(true);
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
        UpButton.IsEnabled = place.Parent is not null;
        SelectFolderItem(place.Root);
        _ = Address.ShowAsync(place);
        return ShowCurrentToolAsync();
    }

    // With no folder added, every button of the top row is disabled except
    // the pane's, and the address bar says "Aucun dossier" (12.11).
    private void ShowEmptyState()
    {
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

    // Re-runs the current tool on the current folder (spec, Open 17).
    private void Refresh()
    {
        if (Place is null) return;
        _ = Address.ShowAsync(Place);
        _ = ShowCurrentToolAsync();
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

    private void OnAddressAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Address.EditAddress();
        args.Handled = true;
    }
}
