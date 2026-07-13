using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsSearchIndex ───────────────────────────────────────────────────────
//
// The shell's in-memory index of every searchable card across every settings page —
// the seam that lets a single search box reach a setting on any page without the box
// knowing the pages. A page (via the App composition root at boot) registers its
// SettingSearchEntry list; the index resolves each entry's display text ONCE, folds
// in the page's coordinates, and thereafter answers queries over the resolved set.
//
// Same lib-exposes-a-static / host-populates-it shape as SettingsModuleRegistry
// beside it: Deckle.Settings owns the index, the composition root fills it, the shell
// stays a pure aggregator with no literal page references. The crucial difference
// from a compose-time index: text is resolved from each module's PRI subtree via
// Loc.GetFrom — exactly as BuildModuleNavItems resolves a nav label — so a card is
// indexed WITHOUT its page ever being composed. Pages compose lazily (only on first
// navigation, NavigationCacheMode.Required), so an index that waited for composition
// would be blind to every unvisited page; resolving from the manifest sidesteps that.
//
// Registration is tolerant by construction. An entry whose header key does not
// resolve is a wiring gap, not a boot-stopper: it is skipped with a Verbose trail and
// the rest of the page still indexes. Nothing here ever throws at boot.
public static class SettingsSearchIndex
{
    // Guards the resolved set against a Register racing a Search read. Every access
    // takes it; Search snapshots under it, then ranks the copy outside the lock.
    private static readonly object _gate = new();
    private static readonly List<SettingSearchHit> _entries = new();

    // Register a module page's searchable cards, deriving the page coordinates every
    // card shares from its nav descriptor: PageTag to navigate to, Glyph to show, and
    // the breadcrumb label resolved from the module's own subtree exactly as
    // BuildModuleNavItems resolves the nav item's text.
    public static void Register(SettingsModuleDescriptor page, IReadOnlyList<SettingSearchEntry> entries)
    {
        string pageLabel = Loc.GetFrom(page.OwningAssembly, page.LabelKey);
        RegisterPage(page.PageTag, page.OwningAssembly, page.Glyph, pageLabel, entries);
    }

    // Register a STATIC shell page's cards — General and its kin have no module
    // descriptor (they are fixed XAML anchors, not registry entries), so the caller
    // supplies the coordinates directly: the page's Type.GetType tag, the assembly its
    // cards' .resw keys live under, its glyph, and its already-resolved nav label.
    public static void RegisterPage(
        string pageTag, string owningAssembly, string glyph, string pageLabel,
        IReadOnlyList<SettingSearchEntry> entries)
    {
        // Resolve outside the lock — Loc touches no shared state here — then commit the
        // batch under it. Re-registering a page replaces its cards rather than doubling
        // them, so a second boot pass (or a reopened window) cannot duplicate hits.
        var resolved = new List<SettingSearchHit>(entries.Count);
        foreach (SettingSearchEntry entry in entries)
        {
            // Label: a bespoke card supplies it inline (LiteralLabel), a composed card
            // resolves "<LabelKey>/Header" from the module subtree. GetFromOptional, not
            // GetFrom, so a missing key returns null instead of a DEBUG marker — the miss
            // is handled here, tolerantly, rather than shipping a "[!…]" hit.
            string? label = entry.LiteralLabel
                ?? Loc.GetFromOptional(owningAssembly, $"{entry.LabelKey}/Header");
            if (string.IsNullOrEmpty(label))
            {
                DeckleSettingsSource.Log.SearchEntrySkipped(pageTag, entry.LabelKey);
                continue;
            }

            // Description is optional on both paths: inline for a bespoke card, otherwise
            // the optional "<LabelKey>/Description" entry (a card may have none).
            string? description = entry.LiteralDescription
                ?? Loc.GetFromOptional(owningAssembly, $"{entry.LabelKey}/Description");

            resolved.Add(new SettingSearchHit
            {
                PageTag = pageTag,
                PageGlyph = glyph,
                PageLabel = pageLabel,
                CardTag = entry.LabelKey,
                Label = label,
                Description = description,
                Keywords = entry.Keywords,
            });
        }

        lock (_gate)
        {
            _entries.RemoveAll(h => h.PageTag == pageTag);
            _entries.AddRange(resolved);
        }
    }

    // Query the index. Returns the top `max` hits in rank order plus the TOTAL number
    // of matches (so the UI can offer a "+N more — refine" hint when total exceeds what
    // it shows). All query tokens are required and matched case-insensitively; an empty
    // query or a non-positive cap matches nothing. Pure over the in-memory set — no
    // allocation beyond the transient score list, so it is cheap to call on each
    // (debounced) keystroke.
    public static (IReadOnlyList<SettingSearchHit> Hits, int Total) Search(string query, int max)
    {
        string[] tokens = query?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (tokens.Length == 0 || max <= 0)
            return (Array.Empty<SettingSearchHit>(), 0);

        SettingSearchHit[] snapshot;
        lock (_gate) snapshot = _entries.ToArray();

        // Score every card that matches ALL tokens. Min is the weakest token's strength
        // (the limiting signal), Best the strongest single token, Sum the aggregate —
        // Order is the registration index, the stable tiebreak.
        var scored = new List<(SettingSearchHit Hit, int Min, int Best, int Sum, int Order)>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            SettingSearchHit hit = snapshot[i];
            int min = int.MaxValue, best = -1, sum = 0;
            bool qualifies = true;

            foreach (string token in tokens)
            {
                int strength = TokenStrength(hit, token);
                if (strength < 0) { qualifies = false; break; }
                if (strength < min) min = strength;
                if (strength > best) best = strength;
                sum += strength;
            }

            if (qualifies) scored.Add((hit, min, best, sum, i));
        }

        int total = scored.Count;

        // Rank: weakest-link tier first (a card every token label-matches beats one that
        // leans on a keyword or description for any token), then the strongest single hit
        // (a label-prefix rises above a mere label-contains), then the aggregate. Equal
        // keys keep registration order — OrderBy is a stable sort — so ties are stable.
        List<SettingSearchHit> hits = scored
            .OrderByDescending(e => e.Min)
            .ThenByDescending(e => e.Best)
            .ThenByDescending(e => e.Sum)
            .ThenBy(e => e.Order)
            .Take(max)
            .Select(e => e.Hit)
            .ToList();

        return (hits, total);
    }

    // How strongly a card matches ONE query token, or -1 when it does not match at all
    // (which disqualifies the card, since every token is required). The tiers are
    // ordered so the spec's ranking falls out of a plain numeric compare:
    //   3 label-prefix > 2 label-contains > 1 keyword > 0 description-contains.
    // A token's best placement wins — a label prefix outranks the same token also
    // sitting in the description.
    private static int TokenStrength(SettingSearchHit hit, string token)
    {
        if (hit.Label.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return 3;
        if (hit.Label.Contains(token, StringComparison.OrdinalIgnoreCase)) return 2;

        foreach (string keyword in hit.Keywords)
            if (keyword.Contains(token, StringComparison.OrdinalIgnoreCase)) return 1;

        if (hit.Description is not null &&
            hit.Description.Contains(token, StringComparison.OrdinalIgnoreCase)) return 0;

        return -1;
    }
}
