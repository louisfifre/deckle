using System.Collections.Generic;

namespace Deckle.Settings;

// ── SettingSearchHit ──────────────────────────────────────────────────────────
//
// One resolved, searchable card in the shell's index — a module's SettingSearchEntry
// after the index has resolved its display text and folded in its parent page's
// coordinates. This is what Search returns and what a suggestion row renders: it
// carries everything the UI needs to show the hit AND to act on it, so the UI never
// reaches back into the entry or the module.
//
// Page coordinates (PageTag / PageGlyph / PageLabel) come from the owning page's
// descriptor at registration and are shared by every card on that page — the glyph
// and breadcrumb a suggestion shows, the tag the shell navigates to. CardTag is the
// card's own identity (== the entry's LabelKey, == the Tag the composer stamped), the
// handle the post-navigation scroll-to walks the visual tree for. Label / Description
// are the resolved, displayable text; Keywords are matched but never shown.
public sealed record SettingSearchHit
{
    // Type.GetType tag of the page this card lives on — the value the shell navigates
    // to, the same PageTag the nav item carries.
    public required string PageTag { get; init; }

    // The parent page's glyph (Glyphs.* character), shown beside the hit so a result
    // reads as belonging to its page.
    public required string PageGlyph { get; init; }

    // The parent page's resolved label — the breadcrumb that situates the hit ("this
    // setting lives on the Recording page").
    public required string PageLabel { get; init; }

    // The card's own runtime identity (== the entry's LabelKey), stamped by the
    // composer onto the built card's Tag. After navigating to PageTag, the scroll-to
    // finds the element whose Tag equals this.
    public required string CardTag { get; init; }

    // The resolved, displayable card label — matched and shown.
    public required string Label { get; init; }

    // The resolved card description, or null when the card has none — matched and
    // shown as a secondary line when present.
    public string? Description { get; init; }

    // English lowercase synonyms — matched against the query, never displayed.
    public IReadOnlyList<string> Keywords { get; init; } = [];
}
