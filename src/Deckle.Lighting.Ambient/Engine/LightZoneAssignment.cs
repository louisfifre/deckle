namespace Deckle.Lighting.Ambient;

// Which screen edge a light follows. Ambient has one mode for the beta, a
// colour per screen edge (Louis, 2026-11-25, grill answer r10q4 B), and the
// lights start where the Hue app's Entertainment setup placed them: "on peut
// les placer déjà". So a light the person never touched follows the edge its
// placement suggests (LightZoneSuggester), and only a pick of theirs, No zone
// included, overrides it.
//
// The suggestion is not written to the settings file: it is read again from
// the area at each start and each opening of the Lights card, so moving a
// light in the Hue app moves it here too while the person has not picked an
// edge for it. A pick is stored, LightZone.None included, which is why a No
// zone pick is kept as a key rather than removed: an absent key means "follow
// the placement" (the 2026-11-23 fix removed the write that brought a light
// set to No zone back on every visit).
internal static class LightZoneAssignment
{
    /// <summary>The edge <paramref name="lightId"/> follows: the stored pick
    /// when there is one, otherwise the suggested edge, otherwise none.</summary>
    public static LightZone Resolve(
        string lightId,
        IReadOnlyDictionary<string, LightZone>? stored,
        IReadOnlyDictionary<string, LightZone>? suggested)
    {
        if (stored is not null && stored.TryGetValue(lightId, out var picked)) return picked;
        if (suggested is not null && suggested.TryGetValue(lightId, out var placed)) return placed;
        return LightZone.None;
    }

    /// <summary>The edge each light of <paramref name="lightIds"/> follows,
    /// by <see cref="Resolve"/>.</summary>
    public static Dictionary<string, LightZone> ResolveAll(
        IEnumerable<string> lightIds,
        IReadOnlyDictionary<string, LightZone>? stored,
        IReadOnlyDictionary<string, LightZone>? suggested)
    {
        var zones = new Dictionary<string, LightZone>(StringComparer.Ordinal);
        foreach (string id in lightIds)
            zones[id] = Resolve(id, stored, suggested);
        return zones;
    }
}
