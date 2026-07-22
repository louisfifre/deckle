using Deckle.Catalog;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Input.PrecisionScroll;

public sealed partial class PrecisionScrollPage : Page
{
    public PrecisionScrollViewModel ViewModel { get; } = new();

    private SettingsComposer? _composer;

    public PrecisionScrollPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _composer = new SettingsComposer(SettingsHost, ViewModel);
        _composer.Compose(ViewModel.SettingsManifest);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
    }
}
