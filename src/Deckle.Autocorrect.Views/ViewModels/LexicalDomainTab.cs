using System.Collections.Generic;
using Deckle.Localization;

namespace Deckle.Autocorrect.Views;

// One tab of the domain selector on LexicalDomainsPage. Immutable — a domain is
// fixed by the build, only its rows carry state — so there is nothing to
// observe here; the page binds through SelectedDomain, whose change swaps the
// whole object.
public sealed class LexicalDomainTab
{
    public string DomainId { get; }

    public string Label { get; }

    public IReadOnlyList<DomainLanguageRow> Languages { get; }

    public LexicalDomainTab(LexicalDomain domain, IReadOnlyList<DomainLanguageRow> languages)
    {
        DomainId = domain.Id;
        Label = Loc.GetFrom(
            AutocorrectSettingsModule.ResourceLibrary, $"{domain.ResourceKey}/Header");
        Languages = languages;
    }
}
