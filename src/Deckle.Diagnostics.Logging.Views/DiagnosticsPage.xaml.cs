using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Localization;

namespace Deckle.Diagnostics.Logging.Views;

// ── DiagnosticsPage ─────────────────────────────────────────────────────────
//
// The one editor of everything Deckle records about itself and of the corpus
// it keeps of what the person says or types. Its own view model owns the Logs
// section (detailed logs, the developer captures under them, the trace and
// its retention) and the Folders section; the corpus folds come from
// DiagnosticsRegistry, each with its module's own model, composed into the
// Corpus section.
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; } = new();

    // Every composer is created before LoadAndSync so its subscription catches
    // Load()'s PropertyChanged, and held in a field so the subscription lives
    // as long as the cached page.
    private SettingsComposer? _logsComposer;
    private SettingsComposer? _foldersComposer;

    // A contribution's model is opened once here and reloaded on every visit
    // (OnNavigatedTo), so a consent flipped elsewhere shows up; its composer
    // resolves the card words from the model's own assembly.
    private readonly List<(SettingsComposer Composer, DiagnosticsSource Source)> _corpus = new();

    public DiagnosticsPage()
    {
        InitializeComponent();
        Header.ResetDetail = Loc.Get("Settings_ResetDiagnosticsDialog_Detail");
        NavigationCacheMode = NavigationCacheMode.Required;

        _logsComposer = new SettingsComposer(LogsHost, ViewModel);
        _logsComposer.Compose(ViewModel.LogsSettings);

        foreach (DiagnosticsContribution contribution in DiagnosticsRegistry.Contributions)
        {
            DiagnosticsSource source = contribution.Open();
            var composer = new SettingsComposer(CorpusHost, source.Model);
            composer.Compose(source.Settings);
            _corpus.Add((composer, source));
        }
        // A run with no module that keeps a corpus shows no empty section.
        CorpusSection.Visibility = _corpus.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _foldersComposer = new SettingsComposer(FoldersHost, ViewModel);
        _foldersComposer.Compose(ViewModel.FoldersSettings);

        foreach (SettingsComposer composer in AllComposers())
            composer.DirtyChanged += (_, _) => GateResetAll();

        LoadAndSync();
    }

    private IEnumerable<SettingsComposer> CorpusComposers() => _corpus.Select(c => c.Composer);

    private IEnumerable<SettingsComposer> AllComposers()
    {
        if (_logsComposer is not null) yield return _logsComposer;
        foreach (SettingsComposer composer in CorpusComposers()) yield return composer;
        if (_foldersComposer is not null) yield return _foldersComposer;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    private void LoadAndSync()
    {
        ViewModel.Load();
        foreach ((_, DiagnosticsSource source) in _corpus) source.Load();
        // Load() raises no PropertyChanged on a clean profile, so no DirtyChanged
        // fires and this call settles the gates.
        GateResetAll();
    }

    private void ResetLogs_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetLogsDefaults();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Logs");
    }

    // Each corpus default is off, so the reset turns every corpus off, through
    // the composers, and TurnOff says the same for any consent a composer's
    // reset skips.
    private void ResetCorpus_Click(object sender, RoutedEventArgs e)
    {
        foreach ((SettingsComposer composer, DiagnosticsSource source) in _corpus)
        {
            composer.ResetAll();
            source.TurnOff();
        }
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Corpus");
    }

    private void ResetFolders_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetFoldersDefaults();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Folders");
    }

    // ── Whole-page "Reset all" ────────────────────────────────────────────────
    private void GateResetAll()
    {
        bool logs = _logsComposer?.IsDirty() ?? false;
        bool corpus = CorpusComposers().Any(c => c.IsDirty());
        bool folders = _foldersComposer?.IsDirty() ?? false;

        LogsSection.IsResetEnabled = logs;
        CorpusSection.IsResetEnabled = corpus;
        FoldersSection.IsResetEnabled = folders;
        Header.IsResetAllEnabled = logs || corpus || folders;
    }

    // The header's destructive confirmation promises every opt-in on the page
    // goes off and the folder returns to its default: the three section resets
    // together, each through its own path.
    private void ResetAllDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetLogsDefaults();
        foreach ((SettingsComposer composer, DiagnosticsSource source) in _corpus)
        {
            composer.ResetAll();
            source.TurnOff();
        }
        ViewModel.ResetFoldersDefaults();
    }
}
