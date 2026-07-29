using System.Collections.Specialized;
using System.ComponentModel;
using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Autocorrect;

// Settings page for the Autocorrect module — the family's landing surface.
// Resolved by the Settings NavigationView via the item Tag
// "Deckle.Autocorrect.AutocorrectPage, Deckle.Autocorrect".
//
// What it holds: the master switch, two drill-in cards to the family's child
// pages (Lexical domains, Apps enrolled), the exclusion register, and the
// module's Diagnostics opt-ins. The per-app and per-domain lists left with their
// surfaces; the exclusion register stayed, because an exclusion crosses every
// domain and every app and belongs to neither child.
//
// Persisted state binds through AutocorrectViewModel — auto-save on every
// change, no OK/Cancel. Two persistence styles sit on the page:
//
//   • The master switch and the Diagnostics opt-ins are COMPOSED: declared as
//     SettingDescriptors in AutocorrectViewModel.Settings.cs and built into their
//     hosts by a SettingsComposer, which carries each card's own inline reset.
//
//   • The exclusion list is BESPOKE — a live collection with add and undo
//     gestures no composer kind models. Its presentation is owned here: the
//     empty-state swap off ExcludedWords.CollectionChanged, and the whole
//     section's visibility gated on the master switch (mask-never-grey) off the
//     VM's Enabled PropertyChanged.
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

        ViewModel.ExcludedWords.CollectionChanged += OnExclusionsChanged;
        // The master switch gates the exclusion section (mask-never-grey), so
        // re-run the visibility pass whenever Enabled changes — the composer's
        // setter raises PropertyChanged, which routes here.
        ViewModel.PropertyChanged += OnViewModelChanged;
        RefreshExclusionsVisibility();
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

    // Same host-only pattern as ComposeSettings, for the Diagnostics section:
    // the composer builds the three observability toggles (declared in
    // AutocorrectViewModel.Settings.cs) into DiagnosticsHost and subscribes to
    // the ViewModel, so they reflect Load() with no code-behind sync.
    private void ComposeDiagnostics()
    {
        _diagnosticsComposer = new SettingsComposer(DiagnosticsHost, ViewModel);
        _diagnosticsComposer.Compose(ViewModel.DiagnosticsSettings);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The master switch is also flipped from the tray menu, the consent
        // dialogs write the telemetry opt-ins, and the exclusion register also
        // grows from the correction inlay — re-pull so a page that sat cached
        // reflects the live model.
        ViewModel.Load();
        RefreshExclusionsVisibility();
    }

    private void OnExclusionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshExclusionsVisibility();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutocorrectViewModel.Enabled))
            RefreshExclusionsVisibility();
    }

    // Enter excludes what is in the box — the gesture the keyboard expects from
    // a field with an adjacent add button. The command owns the empty case, so
    // there is nothing to validate here.
    private void ExclusionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.ExcludeWordCommand.Execute(null);
    }

    // Gates the whole exclusion section on the master switch, then swaps list vs
    // empty-state within it. When the master is off the section is collapsed
    // entirely (mask-never-grey) — a declined feature hides its dependents, it
    // does not grey them. When on, the list and the "nothing excluded yet" line
    // trade places off the count, nested under that gate.
    private void RefreshExclusionsVisibility()
    {
        ExclusionsSection.Visibility =
            ViewModel.Enabled ? Visibility.Visible : Visibility.Collapsed;

        bool any = ViewModel.ExcludedWords.Count > 0;
        ExclusionsList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ExclusionsEmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    // Drill-in: hand the destination's page tag to the shell, which selects the
    // matching rail item and navigates. The module cannot reference the settings
    // shell, so the hop goes through the Catalog delegate the App wires at boot;
    // unwired, the card is inert rather than broken.
    private void OnDomainsCardClick(object sender, RoutedEventArgs e)
        => SettingsNavigation.GoToPage?.Invoke(AutocorrectSettingsModule.LexicalDomainsPageTag);

    private void OnAppsCardClick(object sender, RoutedEventArgs e)
        => SettingsNavigation.GoToPage?.Invoke(AutocorrectSettingsModule.AppsEnrolledPageTag);
}
