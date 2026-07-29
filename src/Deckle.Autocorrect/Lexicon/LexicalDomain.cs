namespace Deckle.Autocorrect;

// ── LexicalDomain ───────────────────────────────────────────────────────────
//
// A field of writing whose vocabulary the corrector can be taught — computing,
// and whatever fabrication delivers next. The domain is the thing the user
// picks; the language is the thing the user turns on inside it. One DomainPack
// is the intersection of the two, so this type carries no artifact and no
// activation state: only the identity and the key its wording resolves under.
//
// The id names the field in full ("computing"), never an abbreviation that could
// read as a BCP-47 tag beside the language tags it travels with. Nothing persists
// it — the settings file keys on the pack id — so it stays free to be spelled for
// the reader.
//
// Shipped lists what the interface may name, not what fabrication dreams of: a
// domain appears here once a pack exists for it in at least one language, so
// the settings page can never offer a tab with nothing behind it.
public sealed record LexicalDomain(string Id, string ResourceKey)
{
    public static IReadOnlyList<LexicalDomain> Shipped { get; } =
    [
        new LexicalDomain("computing", "AutocorrectDomain_Computing"),
    ];
}
