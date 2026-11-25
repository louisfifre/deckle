using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// The Entertainment area picker of the Settings page: which of the bridge's
// groups it offers, and which one it shows selected.
//
// Ambient runs through a Hue Entertainment area only (decided 2026-11-25), so
// the picker offers the groups the bridge types "Entertainment" and nothing
// else: an ordinary room or zone cannot be streamed to.
//
// The stored id (AmbientSettings.HueLastGroupId) is the only truth for the
// selection: an id the picker does not offer selects nothing. The picker used
// to fall back to the first entry and, because the change handler ran, save it
// over the stored id: the choice "reset" on every load (Louis, 2026-11-07).
internal static class HueGroupSelection
{
    /// <summary>The CLIP v1 type the bridge gives the group of an
    /// Entertainment area.</summary>
    public const string EntertainmentType = "Entertainment";

    /// <summary>The groups the picker offers, in the bridge's order.</summary>
    public static IReadOnlyList<HueGroup> EntertainmentAreas(IReadOnlyList<HueGroup> groups)
        => groups
            .Where(g => string.Equals(g.Type, EntertainmentType, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>Index of the stored id in the listed ids, or -1 when there
    /// is no stored id or the picker does not list it.</summary>
    public static int IndexOf(IReadOnlyList<string> listedIds, string? storedId)
    {
        if (string.IsNullOrEmpty(storedId)) return -1;
        for (int i = 0; i < listedIds.Count; i++)
        {
            if (string.Equals(listedIds[i], storedId, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
