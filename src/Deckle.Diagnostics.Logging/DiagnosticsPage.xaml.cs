using System.Collections.Generic;
using System.ComponentModel;
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
// with its own model, composed into the Telemetry section beside the
// mouse-wheel capture. The folders the datasets and logs land in close the
// page, in the Folders section.
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
        ComposeFoldersSection();

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
    // Inside a section the cards with a chevron sit at the top and the flat
    // cards under them, the relative order kept otherwise. The sources are the
    // page's own wheel capture, then the module contributions in registry
    // order, and a source's list may mix folds and leaves (the Transcription
    // corpus is a Group fold beside flat toggles). So each source is split in
    // two: its folds compose into FoldsHost, its leaves into LeavesHost, both
    // in source order. A composer composes once, over one host, so a mixed
    // source gets two composers over the same model; both re-read on its
    // PropertyChanged.
    //
    // A contribution's model is opened once here and reloaded on every visit
    // (OnNavigatedTo), so a consent flipped elsewhere shows up; its composers
    // resolve the card words from the model's own assembly.
    private readonly List<SettingsComposer> _telemetryComposers = new();
    private readonly List<(IReadOnlyList<SettingsComposer> Composers, DiagnosticsSource Source)> _contributions = new();

    private void ComposeTelemetrySection()
    {
        _telemetryComposers.AddRange(ComposeFoldsFirst(ViewModel, ViewModel.TelemetrySettings));

        foreach (DiagnosticsContribution contribution in DiagnosticsRegistry.Contributions)
        {
            DiagnosticsSource source = contribution.Open();
            _contributions.Add((ComposeFoldsFirst(source.Model, source.Settings), source));
        }
    }

    // One composer per non-empty half, so a source with only leaves (the wheel
    // capture) or only folds gets a single composer.
    private IReadOnlyList<SettingsComposer> ComposeFoldsFirst(
        INotifyPropertyChanged model, IReadOnlyList<SettingDescriptor> settings)
    {
        var composers = new List<SettingsComposer>(2);
        List<SettingDescriptor> folds = settings.Where(IsFold).ToList();
        List<SettingDescriptor> leaves = settings.Where(s => !IsFold(s)).ToList();

        if (folds.Count > 0)
        {
            var composer = new SettingsComposer(FoldsHost, model);
            composer.Compose(folds);
            composers.Add(composer);
        }
        if (leaves.Count > 0)
        {
            var composer = new SettingsComposer(LeavesHost, model);
            composer.Compose(leaves);
            composers.Add(composer);
        }
        return composers;
    }

    // The kinds SettingsComposer renders as a SettingsExpander, the cards
    // that carry a chevron.
    private static bool IsFold(SettingDescriptor s)
        => s.Kind is SettingKind.Group or SettingKind.Section or SettingKind.Mode;

    // ── Composed Folders section ──────────────────────────────────────────────
    //
    // The telemetry storage folder, composed above the hand-authored
    // diagnostics folder card. Its own composer, so the section reset and gate
    // cover exactly this one value.
    private SettingsComposer? _telemetryStorageComposer;

    private void ComposeFoldersSection()
    {
        _telemetryStorageComposer = new SettingsComposer(TelemetryStorageHost, ViewModel);
        _telemetryStorageComposer.Compose(ViewModel.TelemetryStorageSettings);
    }

    private IEnumerable<SettingsComposer> AllComposers()
    {
        if (_loggingComposer is not null) yield return _loggingComposer;
        if (_applicationLogComposer is not null) yield return _applicationLogComposer;
        foreach (SettingsComposer composer in TelemetryComposers()) yield return composer;
        if (_telemetryStorageComposer is not null) yield return _telemetryStorageComposer;
    }

    private IEnumerable<SettingsComposer> TelemetryComposers()
    {
        foreach (SettingsComposer composer in _telemetryComposers) yield return composer;
        foreach ((IReadOnlyList<SettingsComposer> composers, _) in _contributions)
            foreach (SettingsComposer composer in composers) yield return composer;
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

    // The section reset clears the wheel capture and the contributed datasets
    // that carry a default (a consent-gated one carries none and stays as the
    // user left it), through the composers, never the storage folder, which
    // the Folders section resets. ViewModel.ResetTelemetryDefaults clears both
    // values, so only the page-wide reset calls it.
    private void ResetTelemetry_Click(object sender, RoutedEventArgs e)
    {
        foreach (SettingsComposer composer in TelemetryComposers()) composer.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Telemetry");
    }

    private void ResetFolders_Click(object sender, RoutedEventArgs e)
    {
        _telemetryStorageComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Folders");
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
        bool folders = _telemetryStorageComposer?.IsDirty() ?? false;

        LoggingSection.IsResetEnabled = logging;
        ApplicationLogSection.IsResetEnabled = applicationLog;
        TelemetrySection.IsResetEnabled = telemetry;
        FoldersSection.IsResetEnabled = folders;
        Header.IsResetAllEnabled = logging || applicationLog || telemetry || folders;
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
        // Clears the wheel capture and the storage folder together.
        ViewModel.ResetTelemetryDefaults();
        // TurnOff once per source, after all of its composers, since a mixed
        // source holds two.
        foreach ((IReadOnlyList<SettingsComposer> composers, DiagnosticsSource source) in _contributions)
        {
            foreach (SettingsComposer composer in composers) composer.ResetAll();
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
