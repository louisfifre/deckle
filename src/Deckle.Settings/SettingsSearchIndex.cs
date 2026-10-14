using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsSearchIndex ───────────────────────────────────────────────────────
//
// The shell's in-memory index of every searchable card across every settings page,
// so a single search box reaches a setting on any page without knowing the pages.
// Text is resolved from each module's PRI subtree via Loc.GetFrom, so a card is
// indexed WITHOUT its page ever being composed. Pages compose lazily (only on first
// navigation, NavigationCacheMode.Required), so an index that waited for composition
// would be blind to every unvisited page.
//
// Registration is tolerant by construction. An entry whose header key does not
// resolve is a wiring gap, not a boot-stopper: it is skipped with a Verbose trail and
// the rest of the page still indexes. Nothing here ever throws at boot.
public static class SettingsSearchIndex
{
    // Register can race a Search read; Search snapshots under the lock, then ranks
    // the copy outside it.
    private static readonly object _gate = new();
    private static readonly List<SettingSearchHit> _entries = new();

    public static void Register(SettingsModuleDescriptor page, IReadOnlyList<SettingSearchEntry> entries)
    {
        string pageLabel = Loc.GetFrom(page.OwningAssembly, page.LabelKey);
        RegisterPage(page.PageTag, page.OwningAssembly, page.Glyph, pageLabel, entries);
    }

    // For a page without its own module descriptor (the static General and
    // Shortcuts pages), or for cards an assembly lends another module's page (the
    // dataset opt-ins each module hands the Diagnostics page): the caller supplies
    // the coordinates directly.
    public static void RegisterPage(
        string pageTag, string owningAssembly, string glyph, string pageLabel,
        IReadOnlyList<SettingSearchEntry> entries)
    {
        // Re-registering replaces the cards rather than doubling them, so a second
        // boot pass cannot duplicate hits. The key is the page AND the owning
        // assembly: several assemblies register cards under the Diagnostics page,
        // and a page-only key would let each registration wipe the one before.
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

            string? description = entry.LiteralDescription
                ?? Loc.GetFromOptional(owningAssembly, $"{entry.LabelKey}/Description");

            resolved.Add(new SettingSearchHit
            {
                PageTag = pageTag,
                PageGlyph = glyph,
                PageLabel = pageLabel,
                OwningAssembly = owningAssembly,
                CardTag = entry.LabelKey,
                Label = label,
                Description = description,
                Keywords = entry.Keywords,
            });
        }

        lock (_gate)
        {
            _entries.RemoveAll(h => h.PageTag == pageTag && h.OwningAssembly == owningAssembly);
            _entries.AddRange(resolved);
        }
    }

    // Returns the top `max` hits in rank order plus the TOTAL number of matches, for
    // the "+N more — refine" hint. Every query token is required.
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
