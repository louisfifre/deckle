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
// Persisted state binds through AutocorrectViewModel — auto-save on every
// change, no OK/Cancel.
public sealed partial class AutocorrectPage : Page
{
    public AutocorrectViewModel ViewModel { get; } = new();

    private SettingsComposer? _settingsComposer;
    private SettingsComposer? _diagnosticsComposer;

    public AutocorrectPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ComposeSettings();
        ComposeDiagnostics();

        ViewModel.ExcludedWords.CollectionChanged += OnExclusionsChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        RefreshExclusionsVisibility();
    }

    // The composer subscribes to the ViewModel, so the composed toggles reflect
    // Load() and their inline reset with no code-behind sync.
    private void ComposeSettings()
    {
        _settingsComposer = new SettingsComposer(MasterHost, ViewModel);
        _settingsComposer.Compose(ViewModel.AutocorrectSettingsManifest);
    }

    private void ComposeDiagnostics()
    {
        _diagnosticsComposer = new SettingsComposer(DiagnosticsHost, ViewModel);
        _diagnosticsComposer.Compose(ViewModel.DiagnosticsSettings);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The master switch is also flipped from the tray menu: re-pull so a
        // page that sat cached reflects the live model.
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

    // Mask-never-grey: with the master off the whole exclusion section is
    // collapsed — a declined feature hides its dependents, it does not grey them.
    // When on, the list and the "nothing excluded yet" line trade places.
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
