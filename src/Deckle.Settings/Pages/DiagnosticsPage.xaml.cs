using System.Diagnostics;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Settings;
using Deckle.Core;
using Deckle.Shell;

namespace Deckle.Settings;

// ── DiagnosticsPage ─────────────────────────────────────────────────────────
//
// Extracted from GeneralPage in slice S2. Hosts the telemetry opt-ins
// (Application log, Microphone, Latency, Corpus + Audio corpus, Storage
// folder) that previously crowded GeneralPage's bottom half. Same
// patterns as GeneralPage : NavigationCacheMode.Required, _initializing
// guard around the initial sync pass, per-toggle consent flow with
// _suppress* re-entry guards.
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; } = new();

    private bool _initializing;

    // Re-entry guards for the consent flows : the Toggled handler reverts
    // the switch when the user cancels the dialog, and that revert would
    // retrigger Toggled in turn.
    private bool _suppressCorpusToggle;
    private bool _suppressAudioCorpusToggle;

    public DiagnosticsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeLoggingSection();
        ComposeTelemetrySection();
        LoadAndSync();
    }

    // ── Composed Logging section ──────────────────────────────────────────────
    //
    // The page only hosts: it hands the host panel and the ViewModel's settings
    // manifest (declared beside the VM in DiagnosticsViewModel.Settings.cs) to
    // the composer, which builds the SettingsCards. The composer subscribes to
    // the ViewModel so the toggles reflect Load() and the section "Reset" without
    // any per-toggle binding here. Composed before LoadAndSync so the
    // subscription catches Load()'s PropertyChanged. Held in a field so the
    // subscription lives as long as the (cached) page.
    private SettingsComposer? _loggingComposer;

    private void ComposeLoggingSection()
    {
        _loggingComposer = new SettingsComposer(LoggingHost, ViewModel);
        _loggingComposer.Compose(ViewModel.LoggingSettings);
    }

    // ── Composed Telemetry card ───────────────────────────────────────────────
    //
    // Same host-only pattern as the Logging section, but a single composable card:
    // the Latency opt-in is the one plain TwoWay toggle in the Telemetry group. Its
    // neighbours (Application log, Microphone, Corpus, Storage folder) each carry a
    // consent dialog, a nested expander, a choice, or a folder path, so they remain
    // hand-authored in the XAML around this host. Composed before LoadAndSync so the
    // composer's PropertyChanged subscription catches Load(); held in a field so the
    // subscription lives as long as the (cached) page.
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
        _initializing = true;
        ViewModel.Load();
        SyncFolderPickerDefault();
        DispatcherQueue.TryEnqueueObserved(
            operation: "init-flag-clear", caller: "diagnostics-page",
            callback: () => _initializing = false,
            rejectSource: "SETTINGS", rejectWhat: "init flag",
            priority: DispatcherQueuePriority.Low);
    }

    // FolderPickerCard.DefaultPath drives the read-only display when
    // TelemetryStorageDirectory is empty + the Open button's target.
    // Resolves to <UserDataRoot>\telemetry\ — what
    // CorpusPaths.GetDefaultDirectoryPath returns, which equals
    // AppPaths.TelemetryDirectory.
    private void SyncFolderPickerDefault()
    {
        TelemetryFolderPicker.DefaultPath = AppPaths.TelemetryDirectory;
    }

    // ── Consent flows ───────────────────────────────────────────────────────
    //
    // Off → On : show a consent dialog. Cancel reverts the toggle (guarded
    // via _suppress*Toggle to avoid re-entering this handler during the
    // revert). On → Off : no confirmation — the user can turn it back on
    // later if needed.
    //
    // Application log and Microphone telemetry once lived here too; they now run
    // the same flow through the composer's confirmOnEnable gate (their descriptors
    // in DiagnosticsViewModel.TelemetrySettings), so only the Corpus pair — nested
    // under a bespoke expander the composer doesn't build — stays hand-authored.

    private async void CorpusLoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _suppressCorpusToggle) return;
        if (!CorpusLoggingToggle.IsOn) return;

        bool confirmed = await CorpusConsentDialog.ShowAsync(this.XamlRoot);
        if (confirmed) return;

        _suppressCorpusToggle = true;
        try { CorpusLoggingToggle.IsOn = false; }
        finally { _suppressCorpusToggle = false; }
    }

    private async void AudioCorpusToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _suppressAudioCorpusToggle) return;
        if (!AudioCorpusToggle.IsOn) return;

        bool confirmed = await AudioCorpusConsentDialog.ShowAsync(this.XamlRoot);
        if (confirmed) return;

        _suppressAudioCorpusToggle = true;
        try { AudioCorpusToggle.IsOn = false; }
        finally { _suppressAudioCorpusToggle = false; }
    }

    private void ResetTelemetry_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetTelemetryDefaults();
    }

    private void ResetLogging_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetLoggingDefaults();
    }

    // Opens the always-on local diagnostics folder (setup + error logs) in
    // Explorer. Same best-effort posture as FolderPickerCard's Open button:
    // ensure the folder exists, shell-execute it, and log a failure rather
    // than surface it — the folder is created eagerly at boot, so this guard
    // is belt-and-braces.
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
            DeckleSettingsSource.Log.FolderPickerFailed();
            DeckleSettingsSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
