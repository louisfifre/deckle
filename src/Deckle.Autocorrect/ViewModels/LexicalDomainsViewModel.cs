using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Autocorrect;

// ViewModel for LexicalDomainsPage. Every user change routes back through
// AutocorrectSettingsService, which owns the write.
//
// The domain catalogue is built once, in the constructor: it is fixed by the
// build (LexicalDomain.Shipped × DomainPack.Shipped), and the page's SelectorBar
// items are built from it — rebuilding the collection on every navigation would
// invalidate them for nothing. Load() only re-seeds what the model can change
// underneath: the master switch this page's section hangs on, and each row's
// activation.
public sealed partial class LexicalDomainsViewModel : ObservableObject
{
    // Mirror of the module's master switch, read-only here.
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // The domains this build can teach, in shipped order. Every domain is listed
    // whether or not the user has met it — what a domain brings must be readable
    // before enabling anything in it.
    public ObservableCollection<LexicalDomainTab> Domains { get; } = new();

    // The SelectorBar is the sole owner: the page mirrors every selection here,
    // the first included, so nothing is seeded from this side. Nothing is
    // persisted either; a tab is a view, not a setting.
    [ObservableProperty]
    public partial LexicalDomainTab? SelectedDomain { get; set; }

    public LexicalDomainsViewModel()
    {
        BuildDomains();
        Load();
    }

    // The dilution figures come from each pack's shipped manifest, read here
    // rather than held live — they are fabrication output and only change when a
    // new pack ships.
    private void BuildDomains()
    {
        var settings = AutocorrectSettingsService.Instance.Current;
        IReadOnlySet<string> systemLanguages = SystemLanguages.Current;
        string dataDir = AutocorrectLexiconArtifacts.DataDirectory;

        foreach (LexicalDomain domain in LexicalDomain.Shipped)
        {
            IReadOnlyList<DomainPack> packs = DomainPack.InDomain(domain.Id);
            var manifests = packs
                .Select(pack => DomainPackManifest.TryLoad(dataDir, pack))
                .ToList();
            var languages = packs
                .Select((pack, index) => new DomainLanguageRow(
                    pack,
                    DomainActivation.IsActive(settings, pack, systemLanguages),
                    manifests[index],
                    OnLanguageToggled))
                .ToList();

            Domains.Add(new LexicalDomainTab(domain, languages, manifests));
        }
    }

    public void Load()
    {
        var settings = AutocorrectSettingsService.Instance.Current;
        IReadOnlySet<string> systemLanguages = SystemLanguages.Current;

        Enabled = settings.Enabled;

        foreach (LexicalDomainTab domain in Domains)
            foreach (DomainLanguageRow language in domain.Languages)
                language.Sync(DomainActivation.IsActive(settings, language.Pack, systemLanguages));
    }

    // Turning a language on changes the effective lexicon, which is merged at
    // engine build — the App notices the key change and rebuilds. Nothing to do
    // here beyond persisting the choice, which is also what turns the row's
    // default from "follows Windows" into an explicit decision.
    private static void OnLanguageToggled(DomainLanguageRow row, bool active)
        => AutocorrectSettingsService.Instance.SetDomainPackActive(row.PackId, active);
}
