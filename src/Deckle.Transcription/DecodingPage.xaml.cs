using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;

namespace Deckle.Transcription;

// The Decoding page of the Transcription module: a host-only page over the
// WhisperViewModel manifests that apply in both pipelines. Its own view model
// instance reloads from the persisted settings on every navigation, like
// WhisperPage's, so an edit made on another page of the module is picked up.
public sealed partial class DecodingPage : Page
{
    public WhisperViewModel ViewModel { get; } = new();

    private readonly List<SettingsComposer> _composers = new();

    public DecodingPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Composed before the first Load() (OnNavigatedTo), so each composer's
        // subscription catches Load()'s refresh; held so they live as long as
        // the cached page.
        Compose(OutputFiltersHost, ViewModel.OutputFilterSettingsManifest);
        Compose(ContextHost, ViewModel.ContextSettingsManifest);
        Compose(DecodingHost, ViewModel.DecodingSettingsManifest);
        Compose(ConfidenceHost, ViewModel.ConfidenceSettingsManifest);
    }

    private void Compose(Panel host, IReadOnlyList<SettingDescriptor> settings)
    {
        var composer = new SettingsComposer(host, ViewModel);
        composer.DirtyChanged += (_, _) => GateResetAll();
        composer.Compose(settings);
        _composers.Add(composer);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
        // Load() may raise no PropertyChanged on a clean profile, so no composer
        // DirtyChanged would fire — settle the gate off the freshly-loaded values.
        GateResetAll();
    }

    // Active-when-dirty over every composer; the header asks before firing.
    private void GateResetAll() =>
        Header.IsResetAllEnabled = _composers.Any(c => c.IsDirty());

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (SettingsComposer composer in _composers) composer.ResetAll();
    }
}
