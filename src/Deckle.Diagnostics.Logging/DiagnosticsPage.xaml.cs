using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Hosts the diagnostics settings that belong to no single module. Every
// toggle is composed from the ViewModel's manifests, so the page carries no
// consent re-entry guard of its own.
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; } = new();

    public DiagnosticsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeLoggingSection();
        ComposeApplicationLogSection();
        ComposeTelemetrySection();

        foreach (var composer in new[]
                 { _loggingComposer, _applicationLogComposer, _telemetryComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

        LoadAndSync();
    }

    // Each section is composed before LoadAndSync so the composer's
    // PropertyChanged subscription catches Load(), and held in a field so the
    // subscription lives as long as the (cached) page.
    private SettingsComposer? _loggingComposer;

    private void ComposeLoggingSection()
    {
        _loggingComposer = new SettingsComposer(LoggingHost, ViewModel);
        _loggingComposer.Compose(ViewModel.LoggingSettings);
    }

    private SettingsComposer? _applicationLogComposer;

    private void ComposeApplicationLogSection()
    {
        _applicationLogComposer = new SettingsComposer(ApplicationLogHost, ViewModel);
        _applicationLogComposer.Compose(ViewModel.ApplicationLogSettings);
    }

    private SettingsComposer? _telemetryComposer;

    private void ComposeTelemetrySection()
    {
        _telemetryComposer = new SettingsComposer(TelemetryHost, ViewModel);
        _telemetryComposer.Compose(ViewModel.TelemetrySettings);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    private void LoadAndSync()
    {
        ViewModel.Load();
        // Settle the page-reset gate off the freshly-loaded values — Load() may raise
        // no PropertyChanged on a clean profile, so no DirtyChanged would fire.
        GateResetAll();
    }

    private void ResetTelemetry_Click(object sender, RoutedEventArgs e)
        => ViewModel.ResetTelemetryDefaults();

    private void ResetLogging_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetLoggingDefaults();
    }

    private void ResetApplicationLog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetApplicationLogDefaults();
    }

    private void GateResetAll()
    {
        ResetAllButton.IsEnabled =
            (_loggingComposer?.IsDirty() ?? false) ||
            (_applicationLogComposer?.IsDirty() ?? false) ||
            (_telemetryComposer?.IsDirty() ?? false);
    }

    // Clears the recorded Application-log consent and the storage override, so
    // it goes through the destructive-confirm gate.
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
    }

    // Best effort: a failure is logged, not surfaced.
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
