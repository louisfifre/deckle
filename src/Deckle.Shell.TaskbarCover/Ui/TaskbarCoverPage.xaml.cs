using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverPage : Page
{
    public TaskbarCoverViewModel ViewModel { get; } = new();

    private readonly SettingsComposer _switchComposer;
    private readonly SettingsComposer _revealComposer;

    public TaskbarCoverPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _switchComposer = new SettingsComposer(SwitchHost, ViewModel);
        _switchComposer.Compose(ViewModel.SwitchManifest);
        _revealComposer = new SettingsComposer(RevealHost, ViewModel);
        _revealComposer.Compose(ViewModel.RevealManifest);

        // Reset all is active only while a composed value differs from its default.
        _switchComposer.DirtyChanged += (_, _) => GateResetAll();
        _revealComposer.DirtyChanged += (_, _) => GateResetAll();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TaskbarCoverViewModel.Enabled)) SyncSectionVisibility();
        };
        SyncSectionVisibility();
        GateResetAll();
    }

    // The page is cached; the tray switch and the hotkey write the same
    // setting meanwhile, so each visit re-reads it.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
        // Load() may raise no PropertyChanged on a clean profile, so no composer
        // DirtyChanged would fire — settle the gate and the section off the
        // freshly-loaded values.
        SyncSectionVisibility();
        GateResetAll();
    }

    // Settings-UX doctrine: what does not apply while the cover is off is
    // hidden, title included, not greyed.
    private void SyncSectionVisibility() =>
        RevealSection.Visibility = ViewModel.Enabled ? Visibility.Visible : Visibility.Collapsed;

    private void GateResetAll()
    {
        Header.IsResetAllEnabled = _switchComposer.IsDirty() || _revealComposer.IsDirty();
    }

    // The header asks before firing (generic wording: nothing user-authored is
    // lost here).
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _switchComposer.ResetAll();
        _revealComposer.ResetAll();
    }
}
