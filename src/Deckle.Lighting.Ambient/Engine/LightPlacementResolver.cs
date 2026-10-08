using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// Resolves the addressable lights of the selected Hue group and suggests a
// border zone for each one. Feeds the per-light rows of the Settings page
// (LightZonesCard); the engine itself only reads the stored assignments.
//
// Split in two on purpose:
//   - MatchEntertainmentArea and SuggestZones are pure: they take the bridge's
//     answers as data, log nothing, and are unit-tested.
//   - ResolveAsync does the I/O against a paired HueBridgeClient (group list,
//     light list, entertainment configurations) and never throws for a
//     bridge failure: a failed call is logged and degrades to "no lights" or
//     "no suggestion", which the card renders as its empty state.
//
// Why an entertainment area at all: when the user already placed the lights
// in the Hue app's entertainment setup, their positions give a zone per light
// for free (LightZoneSuggester). A pure Entertainment group also reports no
// lights over CLIP v1; the matched area's placements then stand in for the
// light list.
internal static class LightPlacementResolver
{
    /// <summary>Finds the entertainment area that describes the group: the
    /// one whose name equals the group's (case-insensitive) wins; otherwise
    /// the one sharing the most light ids with <paramref name="lightIds"/>;
    /// otherwise null.</summary>
    public static EntertainmentAreaMatch? MatchEntertainmentArea(
        string? groupName,
        IReadOnlyCollection<string> lightIds,
        IReadOnlyList<HueEntertainmentArea> areas)
    {
        if (!string.IsNullOrEmpty(groupName))
        {
            foreach (var area in areas)
            {
                if (string.Equals(area.Name, groupName, StringComparison.OrdinalIgnoreCase))
                    return new EntertainmentAreaMatch(area, EntertainmentAreaMatchKind.Name, 0);
            }
        }

        if (lightIds.Count == 0) return null;

        var idSet = new HashSet<string>(lightIds, StringComparer.Ordinal);
        HueEntertainmentArea? best = null;
        int bestOverlap = 0;
        foreach (var area in areas)
        {
            int overlap = 0;
            foreach (var placement in area.LightPlacements)
                if (idSet.Contains(placement.LightId)) overlap++;
            if (overlap > bestOverlap) { best = area; bestOverlap = overlap; }
        }

        return best is null
            ? null
            : new EntertainmentAreaMatch(best, EntertainmentAreaMatchKind.Overlap, bestOverlap);
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

    /// <summary>Lists the group's lights through a REST output bound to it,
    /// then matches an entertainment area for suggestions. The returned
    /// <see cref="LightPlacement.Output"/> stays usable for Identify.</summary>
    public static async Task<LightPlacement> ResolveAsync(
        HueBridgeClient bridge,
        string groupId,
        CancellationToken ct = default)
    {
        var output = new HueRestLightOutput(bridge, groupId);
        var lights = new List<LightDescriptor>();
        try
        {
            await output.ConnectAsync(ct).ConfigureAwait(false);
            lights.AddRange(await output.ListLightsAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCallFailed("list lights", ex);
        }

        string? groupName = await FindGroupNameAsync(bridge, groupId, ct).ConfigureAwait(false);
        DeckleAmbientSource.Log.LightsResolved(groupId, groupName ?? "", lights.Count);

        var areas = await ListAreasAsync(bridge, ct).ConfigureAwait(false);
        var lightIds = lights.ConvertAll(l => l.Id);
        var match = MatchEntertainmentArea(groupName, lightIds, areas);
        LogMatch(areas.Count, match);

        if (lights.Count == 0 && match is { Area.LightPlacements.Count: > 0 })
        {
            DeckleAmbientSource.Log.EntertainmentAreaUsed();
            DeckleAmbientSource.Log.EntertainmentAreaUsedDetail(match.Area.Name, match.Area.LightPlacements.Count);
            foreach (var placement in match.Area.LightPlacements)
                lights.Add(new LightDescriptor(placement.LightId, placement.Name, IsReachable: true));
            lightIds = lights.ConvertAll(l => l.Id);
        }

        var suggestions = match is null
            ? new Dictionary<string, LightZone>()
            : SuggestZones(match.Area, lightIds);
        if (match is not null)
        {
            foreach (var placement in match.Area.LightPlacements)
            {
                if (!suggestions.TryGetValue(placement.LightId, out var zone)) continue;
                DeckleAmbientSource.Log.ZoneSuggested(
                    placement.LightId, zone.ToString(), match.Area.Name, placement.X, placement.Y, placement.Z);
            }
        }

        return new LightPlacement(output, lights, suggestions);
    }

    private static async Task<string?> FindGroupNameAsync(
        HueBridgeClient bridge, string groupId, CancellationToken ct)
    {
        try
        {
            var groups = await bridge.ListGroupsAsync(ct).ConfigureAwait(false);
            foreach (var group in groups)
                if (group.Id == groupId) return group.Name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCallFailed("list groups", ex);
        }
        return null;
    }

    private static async Task<IReadOnlyList<HueEntertainmentArea>> ListAreasAsync(
        HueBridgeClient bridge, CancellationToken ct)
    {
        try
        {
            return await bridge.ListEntertainmentConfigurationsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCallFailed("list ent configs", ex);
            return [];
        }
    }

    private static void LogMatch(int areaCount, EntertainmentAreaMatch? match)
    {
        if (areaCount == 0)
            DeckleAmbientSource.Log.EntertainmentAreaMatched("no_areas", "", "", 0);
        else if (match is null)
            DeckleAmbientSource.Log.EntertainmentAreaMatched("no_match", "", "", 0);
        else
            DeckleAmbientSource.Log.EntertainmentAreaMatched(
                match.Kind == EntertainmentAreaMatchKind.Name ? "name" : "overlap",
                match.Area.Id, match.Area.Name, match.Overlap);
    }

    internal static void LogCallFailed(string op, Exception ex)
    {
        DeckleAmbientSource.Log.LightCallFailed();
        DeckleAmbientSource.Log.LightCallFailedDetail(op, ex.GetType().Name, ex.Message);
    }
}

internal enum EntertainmentAreaMatchKind
{
    Name,
    Overlap,
}

internal sealed record EntertainmentAreaMatch(
    HueEntertainmentArea Area,
    EntertainmentAreaMatchKind Kind,
    int Overlap);

/// <summary>The lights of one group, the zone suggested for each (absent
/// when no entertainment area places it), and the output bound to the group
/// for Identify.</summary>
internal sealed record LightPlacement(
    IMultiLightOutput Output,
    IReadOnlyList<LightDescriptor> Lights,
    IReadOnlyDictionary<string, LightZone> SuggestedZones);
