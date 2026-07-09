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
//
// Extracted from GeneralPage in slice S2. Hosts the cross-cutting telemetry
// opt-ins (Application log, Storage folder) that don't belong to a single
// module. The dictation-scoped opt-ins — Latency and the Corpus + Audio-corpus
// fold — moved to the Dictation (Whisper) page, and the Autocorrect capture
// opt-ins moved to the Autocorrect module's own page (both observe their own
// pipeline). Same NavigationCacheMode.Required pattern as GeneralPage; every
// section is now composed from the ViewModel's manifests — no hand-authored
// toggle remains, so the page carries no consent re-entry guard of its own.
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; } = new();

    public DiagnosticsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeLoggingSection();
        ComposeTelemetrySection();
        ComposeStorageFolderSection();

        // The page-level "Reset all" gate spans every composed section; re-gate on
        // any composer's DirtyChanged.
        foreach (var composer in new[]
                 { _loggingComposer, _telemetryComposer, _storageFolderComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

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
    // Same host-only pattern as the Logging section. One composable row now: the
    // Application log consent opt-in (its off→on dialog carried by a confirmOnEnable
    // gate the composer runs). The Corpus fold and the Autocorrect capture opt-ins
    // that once neighboured it here have moved to their own module pages; the
    // storage-folder path composes into its own host below. Composed before
    // LoadAndSync so the composer's PropertyChanged subscription catches Load(); held
    // in a field so the subscription lives as long as the (cached) page.
    private SettingsComposer? _telemetryComposer;

    private void ComposeTelemetrySection()
    {
        _telemetryComposer = new SettingsComposer(TelemetryHost, ViewModel);
        _telemetryComposer.Compose(ViewModel.TelemetrySettings);
    }

    // ── Composed Storage-folder card ──────────────────────────────────────────
    //
    // The telemetry storage-folder path, a Path descriptor composed through the
    // shared FolderPickerCard (resolved by the composer's PathControlFactory). Its
    // own host — separate from the telemetry toggles above — because it keeps its
    // former slot BELOW them (once the Corpus/Autocorrect expanders sat between).
    // The picker's empty-value
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
        ViewModel.Load();
        // Settle the page-reset gate off the freshly-loaded values — Load() may raise
        // no PropertyChanged on a clean profile, so no DirtyChanged would fire.
        GateResetAll();
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
    // Active-when-dirty gate: enabled while any composed section is dirty. Every
    // section composes now, so the gate follows the composers' DirtyChanged alone
    // (plus one settle after Load()).
    private void GateResetAll()
    {
        ResetAllButton.IsEnabled =
            (_loggingComposer?.IsDirty() ?? false) ||
            (_telemetryComposer?.IsDirty() ?? false) ||
            (_storageFolderComposer?.IsDirty() ?? false);
    }

    // Whole-page reset. Clears the recorded consent (the Application-log opt-in)
    // and the storage override, so it goes through the destructive-confirm gate
    // (Close is the default button). ResetTelemetryDefaults covers the telemetry
    // rows; ResetLoggingDefaults the emission filters. The composers re-sync off the
    // resulting PropertyChanged.
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
