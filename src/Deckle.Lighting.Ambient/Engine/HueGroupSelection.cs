namespace Deckle.Lighting.Ambient;

// Which entry of the listed Hue rooms / entertainment areas the room picker
// shows selected. The stored id (AmbientSettings.HueLastGroupId) is the only
// truth: an id the bridge no longer lists selects nothing. The picker used to
// fall back to the first entry and, because the change handler ran, save it
// over the stored id: the choice "reset" on every Load rooms (Louis,
// 2026-11-07).
internal static class HueGroupSelection
{
    /// <summary>Index of the stored id in the listed ids, or -1 when there
    /// is no stored id or the bridge does not list it.</summary>
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
