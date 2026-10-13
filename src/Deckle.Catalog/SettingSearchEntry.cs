namespace Deckle.Catalog;

// ── SettingSearchEntry ────────────────────────────────────────────────────────
//
// A module's declaration of one searchable card. The shell's search index
// resolves each entry's text once at boot and answers queries without composing
// a page. It lives in Deckle.Catalog so a module declares what is searchable
// without a reference to the shell.
//
// LabelKey is both the .resw key the card's header and description resolve from
// ("<LabelKey>/Header") and the card's runtime identity: the composer stamps it
// onto the built card's Tag, and a search hit brings the element with that Tag
// into view. A hand-authored card sets Tag="<LabelKey>" in its own XAML; without
// it the hit opens the page but scrolls nowhere.
//
// The Literal* fields serve a hand-authored card with no "<LabelKey>/Header"
// entry: the index takes the literal verbatim and does no .resw lookup for that
// field. LabelKey stays required as the Tag identity.
public sealed record SettingSearchEntry
{
    public required string LabelKey { get; init; }

    public string? LiteralLabel { get; init; }

    public string? LiteralDescription { get; init; }

    // English lowercase synonyms this card should match on — matched against the
    // query but NEVER shown in a result. They widen findability ("mic" reaching
    // "Microphone", "theme" reaching "Appearance") without polluting the card's
    // visible text. Empty (the default) when the label and description already
    // carry every term worth matching.
    public string[] Keywords { get; init; } = [];
}
