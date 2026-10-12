using Deckle.Catalog;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverPage : Page
{
    public TaskbarCoverViewModel ViewModel { get; } = new();

    private SettingsComposer? _composer;

    public TaskbarCoverPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _composer = new SettingsComposer(SettingsHost, ViewModel);
        _composer.Compose(ViewModel.SettingsManifest);
    }

    // The page is cached; the tray switch and the hotkey write the same
    // setting meanwhile, so each visit re-reads it.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
    }
}
