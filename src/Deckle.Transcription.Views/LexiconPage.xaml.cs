using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;

namespace Deckle.Transcription.Views;

// The Lexicon page of the Transcription module: a host-only page over the
// lexicon manifest. Its own view model instance reloads from the persisted
// settings on every navigation, like the module's other pages, so an edit made
// on another page is picked up and this page never writes a stale copy.
public sealed partial class LexiconPage : Page
{
    public WhisperViewModel ViewModel { get; } = new();

    // Held so it lives as long as the cached page.
    private readonly SettingsComposer _composer;

    public LexiconPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Composed before the first Load() (OnNavigatedTo), so its subscription
        // catches Load()'s refresh.
        _composer = new SettingsComposer(WordsHost, ViewModel);
        _composer.Compose(ViewModel.LexiconSettingsManifest);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
        WhisperInfoBar.IsOpen = ViewModel.IsWhisperChosen;
        AutocorrectNote.Visibility = WhisperSettingsModule.AutocorrectPresent
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
