using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Core;
using Deckle.Shell;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ── DiagnosticsPage ─────────────────────────────────────────────────────────
//
// Extracted from GeneralPage in slice S2. Hosts the telemetry opt-ins
// (Application log, Microphone, Latency, Corpus + Audio corpus, Storage
// folder) that previously crowded GeneralPage's bottom half. Same
// patterns as GeneralPage : NavigationCacheMode.Required, _initializing
// guard around the initial sync pass, per-toggle consent flow with
// _suppress* re-entry guards. The Application log / Microphone / Latency
// opt-ins and the storage-folder path are composed from the ViewModel's
// manifests; the Corpus and Autocorrect expanders stay hand-authored.
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; } = new();

    private bool _initializing;

    // Re-entry guards for the consent flows : the Toggled handler reverts
    // the switch when the user cancels the dialog, and that revert would
    // retrigger Toggled in turn.
    private bool _suppressAutocorrectDecisionsToggle;
    private bool _suppressAutocorrectTextToggle;

    public DiagnosticsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeLoggingSection();
        ComposeTelemetrySection();
        ComposeCorpusSection();
        ComposeStorageFolderSection();

        // The page-level "Reset all" gate spans every composed section plus the
        // hand-authored Autocorrect toggles; re-gate on any composer's DirtyChanged
        // and on the Autocorrect properties (which no composer tracks).
        foreach (var composer in new[]
                 { _loggingComposer, _telemetryComposer, _corpusComposer, _storageFolderComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

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

    // ── Composed Telemetry section ────────────────────────────────────────────
    //
    // Same host-only pattern as the Logging section. Three composable rows: the
    // Application log and Microphone consent opt-ins (their off→on dialog carried by
    // a confirmOnEnable gate the composer runs) and the plain Latency toggle. Their
    // remaining neighbours (the Corpus and Autocorrect expanders) are nested layouts
    // the composer doesn't build, so they stay hand-authored in the XAML around this
    // host; the storage-folder path composes into its own host below. Composed before
    // LoadAndSync so the composer's PropertyChanged subscription catches Load(); held
    // in a field so the subscription lives as long as the (cached) page.
    private SettingsComposer? _telemetryComposer;

    private void ComposeTelemetrySection()
    {
        _telemetryComposer = new SettingsComposer(TelemetryHost, ViewModel);
        _telemetryComposer.Compose(ViewModel.TelemetrySettings);
    }

    // ── Composed Corpus fold ──────────────────────────────────────────────────
    //
    // The audio-corpus consent fold, now a composed Setting.Group (declared in
    // DiagnosticsViewModel.CorpusSettings) rather than the former hand-authored
    // SettingsExpander. The composer builds the master toggle + its two dependent rows
    // and runs their off→on consent dialogs through the descriptors' confirmOnEnable
    // gate — so the CorpusLoggingToggle_Toggled / AudioCorpusToggle_Toggled handlers and
    // their re-entry guards are gone. Composed before LoadAndSync so the PropertyChanged
    // subscription catches Load() (and the Telemetry "Reset"); held in a field so the
    // subscription lives as long as the (cached) page.
    private SettingsComposer? _corpusComposer;

    private void ComposeCorpusSection()
    {
        _corpusComposer = new SettingsComposer(CorpusHost, ViewModel);
        _corpusComposer.Compose(ViewModel.CorpusSettings);
    }

    // ── Composed Storage-folder card ──────────────────────────────────────────
    //
    // The telemetry storage-folder path, a Path descriptor composed through the
    // shared FolderPickerCard (resolved by the composer's PathControlFactory). Its
    // own host — separate from the telemetry toggles above — because it keeps its
    // former slot BELOW the Corpus/Autocorrect expanders. The picker's empty-value
    // DefaultPath (<UserDataRoot>\telemetry\) now travels in the descriptor's
    // PathArgs, so the old SyncFolderPickerDefault code-behind push is gone. Composed
    // before LoadAndSync so its PropertyChanged subscription catches Load(); held in
    // a field so the subscription lives as long as the (cached) page.
    private SettingsComposer? _storageFolderComposer;

    private void ComposeStorageFolderSection()
    {
        _storageFolderComposer = new SettingsComposer(StorageFolderHost, ViewModel);
        _storageFolderComposer.Compose(ViewModel.StorageFolderSettings);
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
        // Settle the page-reset gate off the freshly-loaded values — Load() may raise
        // no PropertyChanged on a clean profile, so no DirtyChanged would fire.
        GateResetAll();
        DispatcherQueue.TryEnqueueObserved(
            operation: "init-flag-clear", caller: "diagnostics-page",
            callback: () => _initializing = false,
            rejectSource: "SETTINGS", rejectWhat: "init flag",
            priority: DispatcherQueuePriority.Low);
    }

    // ── Consent flows ───────────────────────────────────────────────────────
    //
    // Off → On : show a consent dialog. Cancel reverts the toggle (guarded
    // via _suppress*Toggle to avoid re-entering this handler during the
    // revert). On → Off : no confirmation — the user can turn it back on
    // later if needed.
    //
    // Application log, Microphone and the whole Corpus fold once lived here too; they
    // now run the same flow through the composer's confirmOnEnable gate (their
    // descriptors in DiagnosticsViewModel.TelemetrySettings / CorpusSettings), so only
    // the Autocorrect pair stays hand-authored — a header toggle plus an INDEPENDENT
    // nested toggle (recording verbatim text does not depend on recording decisions),
    // which is neither a dependency Group (it would wrongly mask the text opt-in) nor a
    // flat Section (it would drop the primary opt-in from the header).

    private async void AutocorrectDecisionsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _suppressAutocorrectDecisionsToggle) return;
        if (!AutocorrectDecisionsToggle.IsOn) return;

        bool confirmed = await AutocorrectDecisionsConsentDialog.ShowAsync(this.XamlRoot);
        if (confirmed) return;

        _suppressAutocorrectDecisionsToggle = true;
        try { AutocorrectDecisionsToggle.IsOn = false; }
        finally { _suppressAutocorrectDecisionsToggle = false; }
    }

    private async void AutocorrectTextToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _suppressAutocorrectTextToggle) return;
        if (!AutocorrectTextToggle.IsOn) return;

        bool confirmed = await AutocorrectTextConsentDialog.ShowAsync(this.XamlRoot);
        if (confirmed) return;

        _suppressAutocorrectTextToggle = true;
        try { AutocorrectTextToggle.IsOn = false; }
        finally { _suppressAutocorrectTextToggle = false; }
    }

    // Telemetry reset turns every opt-in off and clears the recorded consent —
    // user-created state, so it goes through the shared destructive-confirm gate
    // (Close is the default button). Logging's reset stays a direct action: it
    // only restores log toggles to defaults, nothing the user authored is lost.
    private async void ResetTelemetry_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_ResetTelemetryDialog_Title"),
                Loc.Get("Settings_ResetTelemetryDialog_Content"),
                Loc.Get("Common_Reset"),
                IsDestructive: true));
        if (!confirmed)
            return;

        ViewModel.ResetTelemetryDefaults();
    }

    private void ResetLogging_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetLoggingDefaults();
    }

    // ── Whole-page "Reset all" ────────────────────────────────────────────────
    //
    // Active-when-dirty gate: enabled while any composed section is dirty OR an
    // Autocorrect opt-in is on (the two hand-authored toggles no composer tracks).
    // Re-evaluated off the composers' DirtyChanged, the VM's PropertyChanged for the
    // Autocorrect rows, and once after Load().
    private void GateResetAll()
    {
        ResetAllButton.IsEnabled =
            (_loggingComposer?.IsDirty() ?? false) ||
            (_telemetryComposer?.IsDirty() ?? false) ||
            (_corpusComposer?.IsDirty() ?? false) ||
            (_storageFolderComposer?.IsDirty() ?? false) ||
            ViewModel.AutocorrectDecisions ||
            ViewModel.AutocorrectText;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The Autocorrect opt-ins are hand-authored — no composer tracks their
        // dirtiness — so the page-reset gate follows them here.
        if (e.PropertyName is nameof(DiagnosticsViewModel.AutocorrectDecisions)
                           or nameof(DiagnosticsViewModel.AutocorrectText))
            GateResetAll();
    }

    // Whole-page reset. Clears recorded consents (telemetry opt-ins, corpus,
    // autocorrect) and the storage override, so it goes through the destructive-
    // confirm gate (Close is the default button). ResetTelemetryDefaults already
    // covers the corpus and autocorrect rows; ResetLoggingDefaults the emission
    // filters. The composers re-sync off the resulting PropertyChanged.
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
        ViewModel.ResetTelemetryDefaults();
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
            DeckleSettingsUxSource.Log.FolderPickerFailed();
            DeckleSettingsUxSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
