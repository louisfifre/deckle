using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deckle.Catalog;
using Microsoft.UI.Xaml;

namespace Deckle.Autocorrect;

// One tab of the domain selector on LexicalDomainsPage, with everything that
// tab shows: the domain's name and its one-line description, the language rows
// it offers, and the indications block that closes the list.
//
// Immutable — a domain is fixed by the build, only its rows carry state — so
// there is nothing to observe here; the page binds through SelectedDomain,
// whose change swaps the whole object.
public sealed class LexicalDomainTab
{
    public string DomainId { get; }

    public string Label { get; }

    public string Description { get; }

    public IReadOnlyList<DomainLanguageRow> Languages { get; }

    // What the domain ships against what fabrication turned away, in prose under
    // the list — the honest counterpart to a list of switches that would
    // otherwise read as pure gain. One sentence per language that ships a
    // manifest; empty and collapsed when none does.
    public string Indications { get; }

    public Visibility IndicationsVisibility =>
        Indications.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public LexicalDomainTab(
        LexicalDomain domain,
        IReadOnlyList<DomainLanguageRow> languages,
        IReadOnlyList<DomainPackManifest?> manifests)
    {
        DomainId = domain.Id;
        Label = Loc.GetFrom(
            AutocorrectSettingsModule.ResourceLibrary, $"{domain.ResourceKey}/Header");
        Description = Loc.GetFrom(
            AutocorrectSettingsModule.ResourceLibrary, $"{domain.ResourceKey}/Description");
        Languages = languages;

        string format = Loc.GetFrom(
            AutocorrectSettingsModule.ResourceLibrary, "LexicalDomainsPage_Indications_Format");
        Indications = string.Join(' ', manifests
            .Select((manifest, index) => manifest is null
                ? null
                : string.Format(
                    CultureInfo.CurrentCulture,
                    format,
                    manifest.ShippedForms,
                    LanguageOf(languages, index),
                    manifest.RefusedForms))
            .OfType<string>());
    }

    // The manifests travel in the same order as the rows, so the row at that
    // index names the language the figures belong to.
    private static string LanguageOf(IReadOnlyList<DomainLanguageRow> languages, int index) =>
        index < languages.Count ? languages[index].LanguageName : string.Empty;
}
