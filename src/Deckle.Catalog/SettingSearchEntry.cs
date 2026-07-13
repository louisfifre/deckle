namespace Deckle.Catalog;

// ── SettingSearchEntry ────────────────────────────────────────────────────────
//
// A module's declaration of ONE searchable card, the search counterpart of the
// SettingsModuleDescriptor a module already contributes for its nav entry. A
// module hands the shell one entry per card it wants findable; the shell's search
// index resolves each entry's display text once at boot and answers queries over
// the resolved set, without ever composing a page.
//
// It lives in Deckle.Catalog, the settings floor every settings-bearing module
// references, so a module DECLARES what is searchable without a reference back to
// the shell — the same reason SettingsModuleDescriptor and the composer live here.
//
// LabelKey is the pivot. It is both the .resw key the card's header/description
// resolve from (via Loc.GetFrom(assembly, "<LabelKey>/Header")) AND the card's
// runtime identity — the exact string the composer stamps onto the built card's
// Tag — so a search hit can navigate to the card's page and then bring THAT card
// into view. Deliberately declarative and text-free: the entry names a key, not a
// string, so the wording stays in the module's own PRI subtree, English-first.
//
// The Literal* escape hatches exist because not every card is composed from a
// descriptor: hand-authored cards (AmbientPage, the LLM profile rows) carry
// hardcoded, often unlocalized text with no "<LabelKey>/Header" entry to resolve.
// Such a card still declares an entry, but supplies its label (and optionally its
// description) inline; when a Literal is set, the index takes it verbatim and does
// NO .resw resolution for that field. LabelKey is still required even then — it
// remains the card's Tag identity for the scroll-to, whether or not a .resw entry
// backs it.
public sealed record SettingSearchEntry
{
    // The .resw key that resolves this card's header/description AND the identity
    // the composer stamps onto the card's Tag. Required even for a bespoke card
    // whose text is supplied via LiteralLabel — it is still the scroll-to handle.
    public required string LabelKey { get; init; }

    // Verbatim label for a bespoke card with no "<LabelKey>/Header" .resw entry.
    // When set, the index uses it as-is and does not resolve the label from .resw.
    // Null (the default) means resolve from the module's PRI subtree, the common
    // path for a composed card.
    public string? LiteralLabel { get; init; }

    // Verbatim description for a bespoke card, same contract as LiteralLabel for
    // the "<LabelKey>/Description" entry. Null means resolve optionally from .resw
    // (a card may legitimately have no description).
    public string? LiteralDescription { get; init; }

    // English lowercase synonyms this card should match on — matched against the
    // query but NEVER shown in a result. They widen findability ("mic" reaching
    // "Microphone", "theme" reaching "Appearance") without polluting the card's
    // visible text. Empty (the default) when the label and description already
    // carry every term worth matching.
    public string[] Keywords { get; init; } = [];
}
