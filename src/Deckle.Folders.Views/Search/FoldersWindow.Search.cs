using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Folders.Views;

// The search button of the top row (spec, Search). Search itself lives in
// PowerToys Command Palette; the button says where it is. Each click asks
// whether Command Palette is installed, off the UI thread, then opens the
// small panel under the button (12.13): absent, it says so and offers
// "Installer PowerToys…" and "Plus tard"; present, one line gives the
// palette's shortcut, since Microsoft documents no link that only opens it
// (SearchRouting.cs). The button is enabled with the rest of the row, once a
// folder is added (12.11).
public sealed partial class FoldersWindow
{
    private ICommandPaletteProbe _commandPalette = new PackagedCommandPaletteProbe();

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await OpenSearchAsync();

    private async Task OpenSearchAsync()
    {
        ICommandPaletteProbe probe = _commandPalette;
        SearchRoute route = await Task.Run(() => SearchRouting.Choose(probe));
        bool installed = route == SearchRoute.CommandPalette;
        SearchAbsentPanel.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        SearchPresentText.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        FlyoutBase.ShowAttachedFlyout(SearchButton);
    }

    // The Store's page, where the person decides; nothing installs here.
    private void OnInstallPowerToysClick(object sender, RoutedEventArgs e)
    {
        SearchFlyout.Hide();
        _ = PowerToysInstallPage.OpenAsync();
    }

    private void OnSearchLaterClick(object sender, RoutedEventArgs e) => SearchFlyout.Hide();

    // What the self-check drives: the panel opened as a click opens it, with
    // a probe that gives the walk's answer, so the walk never depends on
    // what this machine has installed. Nothing presses its buttons.
    public async Task<bool> OpenSearchForReviewAsync(ICommandPaletteProbe probe)
    {
        _commandPalette = probe;
        await OpenSearchAsync();
        return SearchFlyout.IsOpen;
    }

    public void CloseSearchForReview() => SearchFlyout.Hide();

    public bool IsSearchEnabledForReview => SearchButton.IsEnabled;
}
