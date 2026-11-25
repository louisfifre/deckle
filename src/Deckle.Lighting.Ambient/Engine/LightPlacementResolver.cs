using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// Resolves the lights of the selected Entertainment area and suggests a
// border zone for each one. Feeds the per-light rows of the Settings page
// (LightZonesCard); the engine itself only reads the stored assignments.
//
// Ambient drives an Entertainment area only (decided 2026-11-25), so the
// area is the light list: the lights the person placed in the Hue app's
// Entertainment setup, with the position that suggests their screen edge
// (LightZoneSuggester). The area is found the way the output factory finds
// it (HueLightOutputFactory.ResolveEntertainmentAreaAsync), so the card
// lists the lights the stream drives.
//
// Split in two on purpose:
//   - Build and SuggestZones are pure: they take the bridge's answers as
//     data, log nothing, and are unit-tested.
//   - ResolveAsync does the I/O against a paired HueBridgeClient and never
//     throws for a bridge failure: a failed call is logged and degrades to
//     "no lights", which the card renders as its empty state.
internal static class LightPlacementResolver
{
    /// <summary>One row per light the area places, in the area's order, its
    /// reachability read from <paramref name="reachable"/> (true when the
    /// bridge did not report the light), and the zone each placement
    /// suggests.</summary>
    public static (IReadOnlyList<LightDescriptor> Lights, Dictionary<string, LightZone> Suggestions) Build(
        HueEntertainmentArea area,
        IReadOnlyDictionary<string, bool> reachable)
    {
        var lights = new List<LightDescriptor>(area.LightPlacements.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in area.LightPlacements)
        {
            if (!seen.Add(placement.LightId)) continue;
            bool isReachable = !reachable.TryGetValue(placement.LightId, out var r) || r;
            lights.Add(new LightDescriptor(placement.LightId, placement.Name, isReachable));
        }
        return (lights, SuggestZones(area, seen));
    }

    /// <summary>One suggested zone per light of <paramref name="lightIds"/>
    /// that the area places; lights the area does not place get no entry.
    /// A centred light suggests <see cref="LightZone.None"/>.</summary>
    public static Dictionary<string, LightZone> SuggestZones(
        HueEntertainmentArea area,
        IReadOnlyCollection<string> lightIds)
    {
        var idSet = new HashSet<string>(lightIds, StringComparer.Ordinal);
        var suggestions = new Dictionary<string, LightZone>(StringComparer.Ordinal);
        foreach (var placement in area.LightPlacements)
        {
            if (!idSet.Contains(placement.LightId)) continue;
            suggestions[placement.LightId] = LightZoneSuggester.Suggest(placement);
        }
        return suggestions;
    }

    /// <summary>Finds the group's Entertainment area and lists its lights.
    /// The returned <see cref="LightPlacement.Bridge"/> serves Identify.</summary>
    public static async Task<LightPlacement> ResolveAsync(
        HueBridgeClient bridge,
        string groupId,
        CancellationToken ct = default)
    {
        HueEntertainmentArea? area = null;
        try
        {
            area = await HueLightOutputFactory
                .ResolveEntertainmentAreaAsync(bridge, groupId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCallFailed("resolve entertainment area", ex);
        }

        if (area is null)
        {
            DeckleAmbientSource.Log.EntertainmentAreaMatched("no_match", "", "", 0);
            DeckleAmbientSource.Log.LightsResolved(groupId, "", 0);
            return new LightPlacement(bridge, [], new Dictionary<string, LightZone>());
        }
        DeckleAmbientSource.Log.EntertainmentAreaMatched("found", area.Id, area.Name, area.LightPlacements.Count);

        var (lights, suggestions) = Build(area, await ReadReachabilityAsync(bridge, groupId, ct).ConfigureAwait(false));
        DeckleAmbientSource.Log.LightsResolved(groupId, area.Name, lights.Count);
        foreach (var placement in area.LightPlacements)
        {
            if (!suggestions.TryGetValue(placement.LightId, out var zone)) continue;
            DeckleAmbientSource.Log.ZoneSuggested(
                placement.LightId, zone.ToString(), area.Name, placement.X, placement.Y, placement.Z);
        }

        return new LightPlacement(bridge, lights, suggestions);
    }

    // The area carries positions, not reachability; the group's CLIP v1 light
    // list does. A failure leaves every light shown as reachable.
    private static async Task<IReadOnlyDictionary<string, bool>> ReadReachabilityAsync(
        HueBridgeClient bridge, string groupId, CancellationToken ct)
    {
        var reachable = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            foreach (var light in await bridge.ListLightsInGroupAsync(groupId, ct).ConfigureAwait(false))
                reachable[light.Id] = light.Reachable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCallFailed("list lights", ex);
        }
        return reachable;
    }

    internal static void LogCallFailed(string op, Exception ex)
    {
        DeckleAmbientSource.Log.LightCallFailed(op, ex.GetType().Name, ex.Message);
    }
}

/// <summary>The lights of one Entertainment area, the zone suggested for each
/// (absent when its placement suggests none), and the bridge that flashes
/// them for Identify.</summary>
internal sealed record LightPlacement(
    HueBridgeClient Bridge,
    IReadOnlyList<LightDescriptor> Lights,
    IReadOnlyDictionary<string, LightZone> SuggestedZones);
