using Deckle.Catalog;
using Microsoft.UI.Xaml;
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

        // Reset all is active only while a composed value differs from its default.
        _composer.DirtyChanged += (_, _) => GateResetAll();
        GateResetAll();
    }

    // The page is cached; the tray switch and the hotkey write the same
    // setting meanwhile, so each visit re-reads it.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
        // Load() may raise no PropertyChanged on a clean profile, so no composer
        // DirtyChanged would fire — settle the gate off the freshly-loaded values.
        GateResetAll();
    }

    private void GateResetAll()
    {
        Header.IsResetAllEnabled = _composer?.IsDirty() ?? false;
    }

    // The header asks before firing (generic wording: nothing user-authored is
    // lost here).
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _composer?.ResetAll();
    }
}
