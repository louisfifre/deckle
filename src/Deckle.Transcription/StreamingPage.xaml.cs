using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;

namespace Deckle.Transcription;

// The Streaming page of the Transcription module: a host-only page over the
// WhisperViewModel manifests of the streaming pipeline. The two sections under
// the switch show whatever the strategy: the switch picks only the dictation
// pipeline, and files and recovered takes are always cut and trimmed with
// these settings, so hiding them would hide settings still in use. Its own
// view model instance reloads from the persisted settings on every navigation, like
// WhisperPage's, so an edit made on another page of the module is picked up.
public sealed partial class StreamingPage : Page
{
    public WhisperViewModel ViewModel { get; } = new();

    private readonly List<SettingsComposer> _composers = new();

    public StreamingPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Composed before the first Load() (OnNavigatedTo), so each composer's
        // subscription catches Load()'s refresh; held so they live as long as
        // the cached page.
        Compose(SwitchHost, ViewModel.StreamingSwitch);
        Compose(SegmentationHost, ViewModel.SegmentationSettings);
        Compose(VadHost, ViewModel.VadSettings);
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

        // The speech-filtering fold no longer opens with the page: the cut curve
        // is the page's first subject (2026-11-07), and the Silero parameters are
        // the detail under it, one click away. The fold keeps the state the
        // person leaves it in, the page being cached.
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
