using System.Collections.Specialized;
using System.ComponentModel;
using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Autocorrect;

// Settings page for the Autocorrect module. Resolved by the Settings
// NavigationView via the item Tag
// "Deckle.Autocorrect.AutocorrectPage, Deckle.Autocorrect".
//
// Persisted state binds through AutocorrectViewModel — auto-save on every change,
// no OK/Cancel. Two persistence styles sit on the page:
//
//   • The master switch (Enable autocorrect) is COMPOSED: declared as a
//     SettingDescriptor in AutocorrectViewModel.Settings.cs and built into
//     MasterHost by the SettingsComposer, which carries its own inline reset.
//
//   • The per-app list is BESPOKE — a live, runtime-enumerated collection of
//     cards with add/remove/forget gestures that no composer kind models. Its
//     presentation is owned here in code-behind: the empty-state swap (list vs
//     "nothing yet" line) off Apps.CollectionChanged, and the whole section's
//     visibility gated on the master switch (mask-never-grey) off the VM's
//     Enabled PropertyChanged.
public sealed partial class AutocorrectPage : Page
{
    public AutocorrectViewModel ViewModel { get; } = new();

    // Drives the composed master toggle. Held in a field so its subscription to
    // the ViewModel lives as long as the (cached) page — the same host-only
    // pattern TrackpadPage/GeneralPage use.
    private SettingsComposer? _settingsComposer;

    // Drives the composed Diagnostics section (log-activity + the two telemetry
    // opt-ins). Its own composer/host, held in a field for the same lifetime
    // reason as the master one above.
    private SettingsComposer? _diagnosticsComposer;

    public AutocorrectPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ComposeSettings();
        ComposeDiagnostics();

        ViewModel.Apps.CollectionChanged += OnAppsChanged;
        ViewModel.ExcludedWords.CollectionChanged += OnExclusionsChanged;
        // The master switch gates the whole Apps section (mask-never-grey), so
        // re-run the visibility pass whenever Enabled changes — the composer's
        // setter raises PropertyChanged, which routes here.
        ViewModel.PropertyChanged += OnViewModelChanged;
        RefreshAppsVisibility();
    }

    // Host-only, like TrackpadPage: the page hands the host panel and the
    // ViewModel's manifest (declared in AutocorrectViewModel.Settings.cs) to the
    // composer, which builds the master toggle as a SettingsCard. The composer
    // subscribes to the ViewModel, so the toggle reflects Load() (and its inline
    // reset) with no code-behind sync — the change handler still lives in the
    // VM's partial setter (SetEnabled), which the composer drives.
    private void ComposeSettings()
    {
        _settingsComposer = new SettingsComposer(MasterHost, ViewModel);
        _settingsComposer.Compose(ViewModel.AutocorrectSettingsManifest);
    }

    // Same host-only pattern as ComposeSettings, for the always-visible
    // Diagnostics section: the composer builds the three observability toggles
    // (declared in AutocorrectViewModel.Settings.cs) into DiagnosticsHost and
    // subscribes to the ViewModel, so they reflect Load() with no code-behind
    // sync. Unlike the Apps section, this one is not gated on the master switch.
    private void ComposeDiagnostics()
    {
        _diagnosticsComposer = new SettingsComposer(DiagnosticsHost, ViewModel);
        _diagnosticsComposer.Compose(ViewModel.DiagnosticsSettings);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // A decision could have been written by the enrollment toast while the
        // page sat cached — re-pull so the list reflects the live model.
        ViewModel.Load();
        RefreshAppsVisibility();
    }

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshAppsVisibility();

    private void OnExclusionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshAppsVisibility();

    // Enter excludes what is in the box — the gesture the keyboard expects from
    // a field with an adjacent add button. The command owns the empty case, so
    // there is nothing to validate here.
    private void ExclusionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.ExcludeWordCommand.Execute(null);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutocorrectViewModel.Enabled))
            RefreshAppsVisibility();
    }

    // Gates the whole Apps section on the master switch, then swaps list vs
    // empty-state within it. When the master is off the section is collapsed
    // entirely (mask-never-grey) — a declined feature hides its dependents, it
    // does not grey them. When on, the list and the "nothing yet" line trade
    // places off Apps.Count, nested under that gate.
    private void RefreshAppsVisibility()
    {
        bool enabled = ViewModel.Enabled;
        AppsSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        PacksSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ExclusionsSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        bool any = ViewModel.Apps.Count > 0;
        AppsList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        bool anyExcluded = ViewModel.ExcludedWords.Count > 0;
        ExclusionsList.Visibility = anyExcluded ? Visibility.Visible : Visibility.Collapsed;
        ExclusionsEmptyState.Visibility = anyExcluded ? Visibility.Collapsed : Visibility.Visible;
    }
}
