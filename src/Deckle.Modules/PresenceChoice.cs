namespace Deckle.Modules;

// ── PresenceChoice ────────────────────────────────────────────────────────────
//
// The presence choice as the file records it: the ids the person kept (Present)
// and the ids that were on offer when they chose (Known). Present alone cannot
// tell "declined" from "never offered", since both are simply missing from it,
// so a module added to the catalogue after the choice would read as declined and
// vanish at the next boot. Known is what keeps it present.
public sealed record PresenceChoice(IReadOnlySet<string> Present, IReadOnlySet<string> Known)
{
    // Declined means offered and not kept. An id outside Known was never put to
    // the person, so it keeps the default answer of the no-choice case: present.
    public bool Includes(string id) => Present.Contains(id) || !Known.Contains(id);

    // The ids kept over a catalogue, for the selector's initial checkboxes.
    // Present ids are copied as they stand, a stale id included, because the
    // choice used to be handed back untouched.
    public IReadOnlySet<string> Resolve(IEnumerable<string> catalogue)
    {
        var kept = new HashSet<string>(Present, StringComparer.Ordinal);
        foreach (string id in catalogue)
        {
            if (!Known.Contains(id)) kept.Add(id);
        }
        return kept;
    }
}
