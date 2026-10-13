using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Autocorrect;

// Child page of the Autocorrect family — the per-app decision map. Resolved by
// the Settings NavigationView via the item Tag
// "Deckle.Autocorrect.AppsEnrolledPage, Deckle.Autocorrect".
public sealed partial class AppsEnrolledPage : Page
{
    public AppsEnrolledViewModel ViewModel { get; } = new();

    public AppsEnrolledPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ViewModel.Apps.CollectionChanged += OnAppsChanged;
        RefreshAppsVisibility();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // A decision could have been written by the enrollment toast, and the
        // master switch is flipped on the parent page and from the tray — re-pull
        // so the page reflects the live model. Reaching this page always goes
        // through a navigation, so the gate cannot be read stale.
        ViewModel.Load();
        RefreshAppsVisibility();
    }

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshAppsVisibility();

    // Mask-never-grey: with autocorrect off nothing is corrected anywhere, so the
    // per-app section goes away rather than sitting settable-but-inert, and the
    // notice takes its place. When the master is on, the list and the "nothing
    // yet" line trade places off Apps.Count, nested under that gate.
    private void RefreshAppsVisibility()
    {
        bool enabled = ViewModel.Enabled;
        AppsSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DisabledNotice.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        bool any = ViewModel.Apps.Count > 0;
        AppsList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
