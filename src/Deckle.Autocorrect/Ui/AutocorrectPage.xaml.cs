using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Autocorrect;

// Settings page for the Autocorrect module. Resolved by the Settings
// NavigationView via the item Tag
// "Deckle.Autocorrect.AutocorrectPage, Deckle.Autocorrect".
//
// Persisted state (master switch, per-app decisions) binds through
// AutocorrectViewModel — auto-save on every change, no OK/Cancel. The only
// imperative bit of presentation is the empty-state swap: the list and the
// "nothing yet" line trade places off Apps.CollectionChanged, the same
// code-behind-owns-presentation pattern as TrackpadPage.
public sealed partial class AutocorrectPage : Page
{
    public AutocorrectViewModel ViewModel { get; } = new();

    public AutocorrectPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ViewModel.Apps.CollectionChanged += OnAppsChanged;
        RefreshEmptyState();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // A decision could have been written by the enrollment toast while the
        // page sat cached — re-pull so the list reflects the live model.
        ViewModel.Load();
        RefreshEmptyState();
    }

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshEmptyState();

    private void RefreshEmptyState()
    {
        bool any = ViewModel.Apps.Count > 0;
        AppsList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
