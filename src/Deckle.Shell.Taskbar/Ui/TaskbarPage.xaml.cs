using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Shell.Taskbar;

// The taskbar module's own page, shown as General under the Taskbar node. Same
// code-behind shape as TaskbarCoverPage: one composer per host, Reset all gated
// on any composer being dirty. Only the tray card has a default to reset; the
// Recommended card edits Windows' state.
public sealed partial class TaskbarPage : Page
{
    public TaskbarViewModel ViewModel { get; } = new();

    private readonly SettingsComposer[] _composers;

    public TaskbarPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        _composers =
        [
            Compose(TrayHost,  ViewModel.TrayManifest),
            Compose(StartHost, ViewModel.StartManifest),
        ];

        GateResetAll();

        // The promoter outlives this page; follow it only while the page is in
        // a live tree, so a closed Settings window leaves no subscriber.
        Loaded   += (_, _) => ViewModel.AttachStatus(DispatcherQueue);
        Unloaded += (_, _) => ViewModel.DetachStatus();
    }

    // Reset all is active only while a composed value differs from its default.
    private SettingsComposer Compose(Panel host, IReadOnlyList<SettingDescriptor> manifest)
    {
        var composer = new SettingsComposer(host, ViewModel);
        composer.Compose(manifest);
        composer.DirtyChanged += (_, _) => GateResetAll();
        return composer;
    }

    // The page is cached; the settings file may change meanwhile (a restore,
    // another writer) and Windows may have changed the Recommended policy, so
    // each visit re-reads both.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
        // Load() may raise no PropertyChanged on a clean profile, so no composer
        // DirtyChanged would fire; settle the gate off the freshly-loaded values.
        GateResetAll();
    }

    private void GateResetAll()
    {
        // Called from a composer's DirtyChanged while _composers is still being
        // filled; the constructor settles the gate once every composer exists.
        if (_composers is null) return;
        Header.IsResetAllEnabled = _composers.Any(composer => composer.IsDirty());
    }

    // The header asks before firing (generic wording: nothing user-authored is
    // lost here).
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (SettingsComposer composer in _composers) composer.ResetAll();
    }
}