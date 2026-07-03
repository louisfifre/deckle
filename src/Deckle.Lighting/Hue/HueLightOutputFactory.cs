namespace Deckle.Lighting;

public static class HueLightOutputFactory
{
    public static Task<ILightOutput> CreateConnectedPreferredAsync(
        HueBridgeClient client,
        string groupId,
        CancellationToken ct = default)
        => CreateConnectedPreferredAsync(new HueBridgeOutputFactoryBridge(client), groupId, ct);

    public static async Task<ILightOutput> CreatePreferredAsync(
        HueBridgeClient client,
        string groupId,
        CancellationToken ct = default)
        => await CreatePreferredAsync(new HueBridgeOutputFactoryBridge(client), groupId, ct)
            .ConfigureAwait(false);

    internal static async Task<ILightOutput> CreatePreferredAsync(
        IHueOutputFactoryBridge bridge,
        string groupId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ct.ThrowIfCancellationRequested();

        var area = await TryResolveEntertainmentAreaAsync(bridge, groupId, ct).ConfigureAwait(false);
        if (area is not null && !string.IsNullOrWhiteSpace(bridge.Credentials?.ClientKey))
        {
            return bridge.CreateEntertainmentOutput(area, bridge.Credentials.ClientKey);
        }

        return bridge.CreateRestOutput(groupId);
    }

    public static Task<ILightOutput> ConnectPreparedPreferredAsync(
        HueBridgeClient client,
        ILightOutput output,
        string groupId,
        CancellationToken ct = default)
        => ConnectPreparedPreferredAsync(
            new HueBridgeOutputFactoryBridge(client),
            output,
            groupId,
            output is HueEntertainmentLightOutput,
            ct);

    internal static async Task<ILightOutput> CreateConnectedPreferredAsync(
        IHueOutputFactoryBridge bridge,
        string groupId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(bridge.Credentials?.ClientKey))
        {
            var area = await TryResolveEntertainmentAreaAsync(bridge, groupId, ct).ConfigureAwait(false);
            if (area is { Channels.Count: > 0 })
            {
                var entertainment = bridge.CreateEntertainmentOutput(area, bridge.Credentials.ClientKey);
                try
                {
                    await entertainment.ConnectAsync(ct).ConfigureAwait(false);
                    return entertainment;
                }
                catch (OperationCanceledException)
                {
                    await DisposeOutputBestEffortAsync(entertainment).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await DisposeOutputBestEffortAsync(entertainment).ConfigureAwait(false);
                    LogEntertainmentFallback("connect_failed", ex);
                    // The bridge may expose Entertainment but reject the
                    // session at runtime (UDP/DTLS/firmware/firewall).
                    // Keep Ambient usable through the established REST path.
                }
            }
        }

        var rest = bridge.CreateRestOutput(groupId);
        await rest.ConnectAsync(ct).ConfigureAwait(false);
        return rest;
    }

    internal static async Task<ILightOutput> ConnectPreparedPreferredAsync(
        IHueOutputFactoryBridge bridge,
        ILightOutput output,
        string groupId,
        bool allowRestFallback,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ct.ThrowIfCancellationRequested();

        try
        {
            await output.ConnectAsync(ct).ConfigureAwait(false);
            return output;
        }
        catch (OperationCanceledException)
        {
            await DisposeOutputBestEffortAsync(output).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (allowRestFallback)
        {
            await DisposeOutputBestEffortAsync(output).ConfigureAwait(false);
            LogEntertainmentFallback("connect_failed", ex);

            var rest = bridge.CreateRestOutput(groupId);
            await rest.ConnectAsync(ct).ConfigureAwait(false);
            return rest;
        }
    }

    private static async Task<HueEntertainmentArea?> TryResolveEntertainmentAreaAsync(
        IHueOutputFactoryBridge bridge,
        string groupId,
        CancellationToken ct)
    {
        try
        {
            return await ResolveEntertainmentAreaAsync(bridge, groupId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogEntertainmentFallback("catalog_failed", ex);
            // Catalog resolution is best-effort while Entertainment is
            // being proven across bridge firmware versions. REST remains
            // the compatibility path when v2 catalog calls fail.
            return null;
        }
    }

    private static async Task<HueEntertainmentArea?> ResolveEntertainmentAreaAsync(
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
        catch
        {
            // Some Hue Entertainment groups do not expose useful CLIP v1
            // light membership. Name matching above is the primary path
            // for those; overlap is best-effort only.
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

    private static async Task DisposeOutputBestEffortAsync(ILightOutput output)
    {
        try { await output.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }

    private static void LogEntertainmentFallback(string reason, Exception ex)
    {
        DeckleLightingSource.Log.EntertainmentRestFallback();
        DeckleLightingSource.Log.EntertainmentRestFallbackDetail(reason, ex.GetType().Name, ex.Message);
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

        public ILightOutput CreateRestOutput(string groupId)
            => new HueRestLightOutput(_client, groupId);
    }
}

internal interface IHueOutputFactoryBridge
{
    HueCredentials? Credentials { get; }
    Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct);
    Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct);
    Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct);
    ILightOutput CreateEntertainmentOutput(HueEntertainmentArea area, string clientKey);
    ILightOutput CreateRestOutput(string groupId);
}
