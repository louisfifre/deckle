using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// ── DiagnosticsPage ─────────────────────────────────────────────────────────
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

        foreach (var composer in new[]
                 { _loggingComposer, _applicationLogComposer, _telemetryComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

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
        // Load() raises no PropertyChanged on a clean profile, so no DirtyChanged
        // fires and this call settles the gates.
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

    // ── Whole-page "Reset all" ────────────────────────────────────────────────
    private void GateResetAll()
    {
        bool logging = _loggingComposer?.IsDirty() ?? false;
        bool applicationLog = _applicationLogComposer?.IsDirty() ?? false;
        bool telemetry = _telemetryComposer?.IsDirty() ?? false;

        LoggingSection.IsResetEnabled = logging;
        ApplicationLogSection.IsResetEnabled = applicationLog;
        TelemetrySection.IsResetEnabled = telemetry;
        Header.IsResetAllEnabled = logging || applicationLog || telemetry;
    }

    // Clears a recorded consent (the application-log opt-in), so it goes through
    // the destructive confirmation. The composers re-sync off the resulting
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
