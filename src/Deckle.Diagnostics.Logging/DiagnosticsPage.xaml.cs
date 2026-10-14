using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// ── DiagnosticsPage ─────────────────────────────────────────────────────────
//
// The one editor of everything Deckle records about itself. Its own view
// model owns the logging gates, the application log and the shared telemetry
// settings; the module-owned datasets come from DiagnosticsRegistry, each
// with its own model, composed into the Telemetry section between the
// mouse-wheel capture and the storage folder.
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; } = new();

    public DiagnosticsPage()
    {
        InitializeComponent();
        // The page asks with its own wording (it says what the reset clears), so the
        // header's generic confirmation stays off.
        Header.ConfirmsReset = false;
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeLoggingSection();
        ComposeApplicationLogSection();
        ComposeTelemetrySection();

        foreach (SettingsComposer composer in AllComposers())
            composer.DirtyChanged += (_, _) => GateResetAll();

        LoadAndSync();
    }

    // ── Composed Logging section ──────────────────────────────────────────────
    //
    // Every composer is created before LoadAndSync so its subscription catches
    // Load()'s PropertyChanged, and held in a field so the subscription lives as
    // long as the cached page.
    private SettingsComposer? _loggingComposer;

    private void ComposeLoggingSection()
    {
        _loggingComposer = new SettingsComposer(LoggingHost, ViewModel);
        _loggingComposer.Compose(ViewModel.LoggingSettings);
    }

    // ── Composed Application log section ──────────────────────────────────────
    private SettingsComposer? _applicationLogComposer;

    private void ComposeApplicationLogSection()
    {
        _applicationLogComposer = new SettingsComposer(ApplicationLogHost, ViewModel);
        _applicationLogComposer.Compose(ViewModel.ApplicationLogSettings);
    }

    // ── Composed Telemetry section ────────────────────────────────────────────
    //
    // Three hosts in one section: the page's own wheel capture, the module
    // contributions in registry order, the storage folder. A contribution's
    // model is opened once here and reloaded on every visit (OnNavigatedTo), so
    // a consent flipped elsewhere shows up; its composer resolves the card
    // words from the model's own assembly.
    private SettingsComposer? _telemetryComposer;
    private SettingsComposer? _telemetryStorageComposer;
    private readonly List<(SettingsComposer Composer, DiagnosticsSource Source)> _contributions = new();

    private void ComposeTelemetrySection()
    {
        _telemetryComposer = new SettingsComposer(TelemetryHost, ViewModel);
        _telemetryComposer.Compose(ViewModel.TelemetrySettings);

        foreach (DiagnosticsContribution contribution in DiagnosticsRegistry.Contributions)
        {
            DiagnosticsSource source = contribution.Open();
            var composer = new SettingsComposer(ContributionsHost, source.Model);
            composer.Compose(source.Settings);
            _contributions.Add((composer, source));
        }

        _telemetryStorageComposer = new SettingsComposer(TelemetryStorageHost, ViewModel);
        _telemetryStorageComposer.Compose(ViewModel.TelemetryStorageSettings);
    }

    private IEnumerable<SettingsComposer> AllComposers()
    {
        if (_loggingComposer is not null) yield return _loggingComposer;
        if (_applicationLogComposer is not null) yield return _applicationLogComposer;
        if (_telemetryComposer is not null) yield return _telemetryComposer;
        foreach ((SettingsComposer composer, _) in _contributions) yield return composer;
        if (_telemetryStorageComposer is not null) yield return _telemetryStorageComposer;
    }

    private IEnumerable<SettingsComposer> TelemetryComposers()
    {
        if (_telemetryComposer is not null) yield return _telemetryComposer;
        foreach ((SettingsComposer composer, _) in _contributions) yield return composer;
        if (_telemetryStorageComposer is not null) yield return _telemetryStorageComposer;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    private void LoadAndSync()
    {
        ViewModel.Load();
        foreach ((_, DiagnosticsSource source) in _contributions) source.Load();
        // Load() raises no PropertyChanged on a clean profile, so no DirtyChanged
        // fires and this call settles the gates.
        GateResetAll();
    }

    // The section reset clears the page's own telemetry values and the
    // contributed datasets that carry a default (a consent-gated one carries
    // none and stays as the user left it).
    private void ResetTelemetry_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetTelemetryDefaults();
        foreach ((SettingsComposer composer, _) in _contributions) composer.ResetAll();
    }

    private void ResetLogging_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetLoggingDefaults();
    }

    private void ResetApplicationLog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetApplicationLogDefaults();
    }

    // ── Whole-page "Reset all" ────────────────────────────────────────────────
    private void GateResetAll()
    {
        bool logging = _loggingComposer?.IsDirty() ?? false;
        bool applicationLog = _applicationLogComposer?.IsDirty() ?? false;
        bool telemetry = TelemetryComposers().Any(c => c.IsDirty());

        LoggingSection.IsResetEnabled = logging;
        ApplicationLogSection.IsResetEnabled = applicationLog;
        TelemetrySection.IsResetEnabled = telemetry;
        Header.IsResetAllEnabled = logging || applicationLog || telemetry;
    }

    // Clears recorded consents (the application-log opt-in and every
    // contributed dataset), so it goes through the destructive confirmation,
    // whose words promise every opt-in on the page goes off. The composers'
    // ResetAll skips a consent, which carries no default, so each contribution
    // also turns its own datasets off. The composers re-sync off the resulting
    // PropertyChanged.
    private async void ResetAllDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_ResetDiagnosticsDialog_Title"),
                Loc.Get("Settings_ResetDiagnosticsDialog_Content"),
                Loc.Get("Common_Reset"),
                IsDestructive: true));
        if (!confirmed) return;

        ViewModel.ResetLoggingDefaults();
        ViewModel.ResetApplicationLogDefaults();
        ViewModel.ResetTelemetryDefaults();
        foreach ((SettingsComposer composer, DiagnosticsSource source) in _contributions)
        {
            composer.ResetAll();
            source.TurnOff();
        }
    }

    // Best effort, like FolderPickerCard's Open button: a failure is logged, not
    // surfaced.
    private void OpenDiagnosticsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DiagnosticsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DiagnosticsDirectory,
                UseShellExecute = true,
            });
        }
        catch (System.Exception ex)
        {
            DeckleSettingsUxSource.Log.FolderPickerFailed();
            DeckleSettingsUxSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
