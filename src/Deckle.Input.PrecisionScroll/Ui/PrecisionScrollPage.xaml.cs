using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Input.PrecisionScroll;

public sealed partial class PrecisionScrollPage : Page
{
    public PrecisionScrollViewModel ViewModel { get; } = new();

    private readonly SettingsComposer _switchComposer;
    private readonly SettingsComposer _scrollingComposer;

    public PrecisionScrollPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Header.ResetDetail = Loc.Get("SettingsPageHeader_ResetKeepsSwitch");
        _switchComposer = new SettingsComposer(SwitchHost, ViewModel);
        _switchComposer.Compose(ViewModel.SwitchManifest);
        _scrollingComposer = new SettingsComposer(ScrollingHost, ViewModel);
        _scrollingComposer.Compose(ViewModel.ScrollingManifest);

        // Reset all is active only while a composed value differs from its default.
        _switchComposer.DirtyChanged += (_, _) => GateResetAll();
        _scrollingComposer.DirtyChanged += (_, _) => GateResetAll();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PrecisionScrollViewModel.Enabled)) SyncSectionVisibility();
        };
        SyncSectionVisibility();
        GateResetAll();
    }

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

    // Settings-UX doctrine: what does not apply while the module is off is
    // hidden, title included, not greyed.
    private void SyncSectionVisibility() =>
        ScrollingSection.Visibility = ViewModel.Enabled ? Visibility.Visible : Visibility.Collapsed;

    // The module switch (_switchComposer) is the person's choice, not a value:
    // neither the gate nor the reset reads it (2026-11-07). The header asks
    // before firing.
    private void GateResetAll()
    {
        Header.IsResetAllEnabled = _scrollingComposer.IsDirty();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _scrollingComposer.ResetAll();
    }
}
