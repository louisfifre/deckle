using System.Collections.Generic;

namespace Deckle.Settings;

// ── SettingSearchHit ──────────────────────────────────────────────────────────
//
// One resolved, searchable card in the shell's index — a module's SettingSearchEntry
// after the index has resolved its display text and folded in its parent page's
// coordinates. It carries everything the UI needs to show the hit AND to act on it,
// so the UI never reaches back into the entry or the module.
public sealed record SettingSearchHit
{
    // Type.GetType tag of the page this card lives on — the same PageTag the nav
    // item carries.
    public required string PageTag { get; init; }

    public required string PageGlyph { get; init; }

    public required string PageLabel { get; init; }

    // The assembly whose .resw the card's words resolve from. Usually the page's
    // own, but a page can show cards other assemblies lend it (the Diagnostics
    // page holds each module's dataset opt-ins), so the index keys a
    // registration on the page and this assembly together.
    public string OwningAssembly { get; init; } = "";

    // The card's own runtime identity (== the entry's LabelKey), stamped by the
    // composer onto the built card's Tag. After navigating to PageTag, the scroll-to
    // finds the element whose Tag equals this.
    public required string CardTag { get; init; }

    public required string Label { get; init; }

    public string? Description { get; init; }

    // English lowercase synonyms — matched against the query, never displayed.
    public IReadOnlyList<string> Keywords { get; init; } = [];
}
