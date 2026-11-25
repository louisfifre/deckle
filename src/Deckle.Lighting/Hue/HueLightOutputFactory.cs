using System.Diagnostics.CodeAnalysis;

namespace Deckle.Lighting;

// Builds the one output Ambient drives: a Hue Entertainment stream over the
// area that backs the selected group. There is no other path. A bridge with
// no client key (paired before Entertainment existed) or a group no
// Entertainment area describes cannot stream, and the factory says so with a
// HueEntertainmentUnavailableException naming the reason, so the start fails
// with a cause the Settings page can show instead of driving the lights
// another, slower way in silence (decided 2026-11-25).
public static class HueLightOutputFactory
{
    private const string EntertainmentOutputName = "hue_entertainment";

    /// <summary>Builds the Entertainment output for the group, without
    /// connecting it, and returns it with the area it streams to, whose light
    /// placements give each light its suggested screen edge. Throws
    /// <see cref="HueEntertainmentUnavailableException"/> when the bridge has
    /// no client key or no Entertainment area describes the group; a failed
    /// catalog request propagates as it came.</summary>
    public static Task<(ILightOutput Output, HueEntertainmentArea Area)> CreateAsync(
        HueBridgeClient client,
        string groupId,
        CancellationToken ct = default)
        => CreateAsync(new HueBridgeOutputFactoryBridge(client), groupId, ct);

    internal static async Task<(ILightOutput Output, HueEntertainmentArea Area)> CreateAsync(
        IHueOutputFactoryBridge bridge,
        string groupId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ct.ThrowIfCancellationRequested();

        if (!TryGetClientKey(bridge, out string? clientKey))
        {
            throw new HueEntertainmentUnavailableException(
                HueEntertainmentUnavailableReason.NoClientKey,
                "The Hue bridge was paired without an Entertainment client key; pair it again.");
        }

        var area = await ResolveEntertainmentAreaAsync(bridge, groupId, ct).ConfigureAwait(false);
        if (area is not { Channels.Count: > 0 })
        {
            throw new HueEntertainmentUnavailableException(
                HueEntertainmentUnavailableReason.NoEntertainmentArea,
                $"No Hue Entertainment area with lights describes group '{groupId}'.");
        }

        DeckleLightingSource.Log.LightOutputSelected(EntertainmentOutputName, "entertainment_area_found");
        return (bridge.CreateEntertainmentOutput(area, clientKey), area);
    }

    /// <summary>Connects an output <see cref="CreateAsync(HueBridgeClient, string, CancellationToken)"/>
    /// built. On failure the output is disposed, which hands the lights back,
    /// and the failure propagates.</summary>
    public static async Task ConnectAsync(ILightOutput output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ct.ThrowIfCancellationRequested();

        try
        {
            await output.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await DisposeOutputBestEffortAsync(output).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The Entertainment area that describes the group: the one
    /// whose name equals the group's (case-insensitive), which is how the
    /// bridge names the CLIP v1 group of an Entertainment area; otherwise the
    /// one sharing the most lights with the group; otherwise null.</summary>
    public static Task<HueEntertainmentArea?> ResolveEntertainmentAreaAsync(
        HueBridgeClient client,
        string groupId,
        CancellationToken ct = default)
        => ResolveEntertainmentAreaAsync(new HueBridgeOutputFactoryBridge(client), groupId, ct);

    internal static async Task<HueEntertainmentArea?> ResolveEntertainmentAreaAsync(
        IHueOutputFactoryBridge bridge,
        string groupId,
        CancellationToken ct)
    {
        var areas = await bridge.ListEntertainmentConfigurationsAsync(ct).ConfigureAwait(false);
        if (areas.Count == 0) return null;

        HueGroup? selectedGroup = null;
        var groups = await bridge.ListGroupsAsync(ct).ConfigureAwait(false);
        foreach (var group in groups)
        {
            if (group.Id == groupId)
            {
                selectedGroup = group;
                break;
            }
        }

        if (selectedGroup is not null)
        {
            foreach (var area in areas)
            {
                if (string.Equals(area.Name, selectedGroup.Name, StringComparison.OrdinalIgnoreCase))
                    return area;
            }
        }

        IReadOnlyList<HueLight> groupLights = [];
        try
        {
            groupLights = await bridge.ListLightsInGroupAsync(groupId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Some Hue Entertainment groups do not expose useful CLIP v1
            // light membership. Name matching above is the primary path
            // for those; overlap is best-effort only.
            DeckleLightingSource.Log.HueStepFailedDetail("group_lights_lookup", ex.GetType().Name, ex.Message);
        }

        if (groupLights.Count == 0) return null;

        var lightIds = new HashSet<string>(groupLights.Select(l => l.Id), StringComparer.Ordinal);
        HueEntertainmentArea? best = null;
        int bestOverlap = 0;
        foreach (var area in areas)
        {
            int overlap = 0;
            foreach (var channel in area.Channels)
            {
                if (channel.LightId is not null && lightIds.Contains(channel.LightId))
                    overlap++;
            }

            if (overlap > bestOverlap)
            {
                best = area;
                bestOverlap = overlap;
            }
        }

        return bestOverlap > 0 ? best : null;
    }

    private static bool TryGetClientKey(IHueOutputFactoryBridge bridge, [NotNullWhen(true)] out string? clientKey)
    {
        clientKey = bridge.Credentials?.ClientKey;
        return !string.IsNullOrWhiteSpace(clientKey);
    }

    private static async Task DisposeOutputBestEffortAsync(ILightOutput output)
    {
        try { await output.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            DeckleLightingSource.Log.HueStepFailedDetail("output_dispose", ex.GetType().Name, ex.Message);
        }
    }

    private sealed class HueBridgeOutputFactoryBridge : IHueOutputFactoryBridge
    {
        private readonly HueBridgeClient _client;

        public HueBridgeOutputFactoryBridge(HueBridgeClient client)
        {
            _client = client;
        }

        public HueCredentials? Credentials => _client.Credentials;

        public Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct)
            => _client.ListEntertainmentConfigurationsAsync(ct);

        public Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct)
            => _client.ListGroupsAsync(ct);

        public Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct)
            => _client.ListLightsInGroupAsync(groupId, ct);

        public ILightOutput CreateEntertainmentOutput(HueEntertainmentArea area, string clientKey)
            => new HueEntertainmentLightOutput(_client, area, clientKey);
    }
}

internal interface IHueOutputFactoryBridge
{
    HueCredentials? Credentials { get; }
    Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct);
    Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct);
    Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct);
    ILightOutput CreateEntertainmentOutput(HueEntertainmentArea area, string clientKey);
}

/// <summary>Why the bridge cannot stream to the selected group.</summary>
public enum HueEntertainmentUnavailableReason
{
    /// <summary>The bridge was paired without the Entertainment client key,
    /// which only a new pairing obtains.</summary>
    NoClientKey,

    /// <summary>No Entertainment area with lights describes the selected
    /// group: the area was deleted or emptied, or the group is an ordinary
    /// room or zone chosen before Ambient ran through Entertainment only.</summary>
    NoEntertainmentArea,
}

/// <summary>Ambient cannot start because the bridge cannot stream to the
/// selected group; <see cref="Reason"/> says why.</summary>
public sealed class HueEntertainmentUnavailableException : InvalidOperationException
{
    public HueEntertainmentUnavailableException(HueEntertainmentUnavailableReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public HueEntertainmentUnavailableReason Reason { get; }
}
