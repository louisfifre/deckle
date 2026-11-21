using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;

namespace Deckle.Transcription.Views;

// The Decoding page of the Transcription module: a host-only page over the
// WhisperViewModel manifests that apply in both pipelines. Its own view model
// instance reloads from the persisted settings on every navigation, like
// WhisperPage's, so an edit made on another page of the module is picked up.
public sealed partial class DecodingPage : Page
{
    public WhisperViewModel ViewModel { get; } = new();

    private readonly List<SettingsComposer> _composers = new();

    // The composer of the silence guards: the one fold of this page that Parakeet
    // shares with Whisper (the Silero trim).
    private SettingsComposer? _silenceGuardsComposer;

    public DecodingPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Composed before the first Load() (OnNavigatedTo), so each composer's
        // subscription catches Load()'s refresh; held so they live as long as
        // the cached page.
        Compose(DecodingHost, ViewModel.DecodingSettingsManifest);
        Compose(ConfidenceHost, ViewModel.ConfidenceSettingsManifest);
        _silenceGuardsComposer = Compose(SilenceGuardsHost, ViewModel.SilenceGuardSettings);
        Compose(OutputFiltersHost, ViewModel.OutputFilterSettingsManifest);
        Compose(ContextHost, ViewModel.ContextSettingsManifest);
    }

    private SettingsComposer Compose(Panel host, IReadOnlyList<SettingDescriptor> settings)
    {
        var composer = new SettingsComposer(host, ViewModel);
        composer.DirtyChanged += (_, _) => GateResetAll();
        composer.Compose(settings);
        _composers.Add(composer);
        return composer;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
        ApplyEngineVisibility();
        // Load() may raise no PropertyChanged on a clean profile, so no composer
        // DirtyChanged would fire — settle the gate off the freshly-loaded values.
        GateResetAll();
    }

    // Whisper parameters apply to Whisper alone: while another engine is chosen
    // their sections are hidden (a setting that does not apply is hidden, never
    // greyed) and a note says why. Their values stay as they were.
    private void ApplyEngineVisibility()
    {
        Visibility whisper = ViewModel.IsWhisperChosen ? Visibility.Visible : Visibility.Collapsed;
        DecodingSection.Visibility = whisper;
        ConfidenceSection.Visibility = whisper;
        OutputFiltersSection.Visibility = whisper;
        ContextSection.Visibility = whisper;
        WhisperOnlyInfoBar.IsOpen = !ViewModel.IsWhisperChosen;
    }

    // The composers the person can see: every one for Whisper, the silence
    // guards alone otherwise, so Reset all never rewrites a value the page hides.
    private IEnumerable<SettingsComposer> VisibleComposers() =>
        ViewModel.IsWhisperChosen
            ? _composers
            : _composers.Where(c => c == _silenceGuardsComposer);

    // Active-when-dirty over the visible composers; the header asks before firing.
    private void GateResetAll() =>
        Header.IsResetAllEnabled = VisibleComposers().Any(c => c.IsDirty());

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (SettingsComposer composer in VisibleComposers().ToList()) composer.ResetAll();
    }
}
