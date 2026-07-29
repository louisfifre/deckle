using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Autocorrect;

// Child page of the Autocorrect family — the lexical domains. Resolved by the
// Settings NavigationView via the item Tag
// "Deckle.Autocorrect.LexicalDomainsPage, Deckle.Autocorrect".
//
// One bespoke surface, a live catalogue no composer kind models: the domain tabs
// with their language switches. This code-behind owns what presentation it needs
// — filling the SelectorBar from the catalogue, mirroring its selection into the
// ViewModel, and collapsing the whole section while the master switch is off
// (mask-never-grey), with one line in its place.
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

    // The tabs are a runtime-enumerated catalogue (LexicalDomain.Shipped), so
    // the items are built here rather than declared: SelectorBar has no
    // ItemsSource. The first is selected on load, which is also what the
    // control's selection-follows-focus behaviour expects — a bar with nothing
    // selected would leave the page's whole domain block empty. Selecting it here
    // is also what gives the ViewModel its first SelectedDomain: the bar owns the
    // selection outright, so nothing seeds it from the other side.
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
