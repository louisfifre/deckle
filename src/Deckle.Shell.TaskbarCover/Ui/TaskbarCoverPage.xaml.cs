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
        Header.ResetDetail = Loc.Get("SettingsPageHeader_ResetKeepsSwitch");
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
            // The saved distance: the release of a drag, a typed figure, a
            // reset, or a reload on the next visit.
            else if (args.PropertyName == nameof(TaskbarCoverViewModel.ApproachDistanceDip))
                Preview.ApproachDistanceDip = ViewModel.ApproachDistanceDip;
        };
        // Each detent of a drag, before the release saves it.
        ViewModel.ApproachDistancePreviewed += dip => Preview.ApproachDistanceDip = dip;
        Preview.ApproachDistanceDip = ViewModel.ApproachDistanceDip;
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
    // hidden, title included, not greyed. The preview re-reads the taskbar
    // each time the section shows, on a visit or as the cover turns on, so it
    // draws the screen as it is then, not as it was at the page's creation.
    private void SyncSectionVisibility()
    {
        bool shown = ViewModel.Enabled;
        RevealSection.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown)
        {
            Preview.ApproachDistanceDip = ViewModel.ApproachDistanceDip;
            Preview.Refresh();
        }
    }

    // The module switch (_switchComposer) is the person's choice, not a value:
    // neither the gate nor the reset reads it (2026-11-07). The header asks
    // before firing.
    private void GateResetAll()
    {
        Header.IsResetAllEnabled = _revealComposer.IsDirty();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _revealComposer.ResetAll();
    }
}
