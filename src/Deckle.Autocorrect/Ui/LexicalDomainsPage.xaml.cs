using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Autocorrect;

// Child page of the Autocorrect family — the lexical domains. Resolved by the
// Settings NavigationView via the item Tag
// "Deckle.Autocorrect.LexicalDomainsPage, Deckle.Autocorrect".
public sealed partial class LexicalDomainsPage : Page
{
    public LexicalDomainsViewModel ViewModel { get; } = new();

    public LexicalDomainsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        BuildDomainSelector();
        RefreshGate();
    }

    // SelectorBar has no ItemsSource, so the tabs are built here from the
    // catalogue. The first is selected on load: with the control's
    // selection-follows-focus behaviour, a bar with nothing selected would leave
    // the whole domain block empty. The bar owns the selection, so this is also
    // what gives the ViewModel its first SelectedDomain.
    private void BuildDomainSelector()
    {
        foreach (LexicalDomainTab domain in ViewModel.Domains)
            DomainSelector.Items.Add(new SelectorBarItem { Text = domain.Label, Tag = domain });

        DomainSelector.SelectedItem = DomainSelector.Items.FirstOrDefault();
    }

    private void OnDomainChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        => ViewModel.SelectedDomain = sender.SelectedItem?.Tag as LexicalDomainTab;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The master switch is flipped on the parent page and from the tray, and a
        // language row's default follows the settings file — re-pull so a page
        // that sat cached reflects the live model. Reaching this page always goes
        // through a navigation, so the gate cannot be read stale.
        ViewModel.Load();
        RefreshGate();
    }

    // Mask-never-grey: with autocorrect off, no domain reaches the corrector, so
    // the whole section goes away rather than sitting settable-but-inert, and the
    // notice takes its place — the one line that says why there is nothing here.
    private void RefreshGate()
    {
        bool enabled = ViewModel.Enabled;
        DomainsSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DisabledNotice.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }
}
