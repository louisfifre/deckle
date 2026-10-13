namespace Deckle.Lighting;

// ILightOutput / IMultiLightOutput over CLIP v1 REST, bound to one group
// on a paired HueBridgeClient.
//
// Ownership. The HueBridgeClient comes from outside and is NOT disposed
// here: the caller paired the bridge and knows when the credentials should
// be released. Several outputs can share one client; the HttpClient
// underneath is thread-safe for parallel requests.
//
// ConnectAsync refuses an unpaired client rather than pairing lazily: the
// link-button flow is interactive and does not belong behind this
// abstraction. ListLightsAsync caches the bridge response until the next
// ConnectAsync.
public sealed class HueRestLightOutput : IMultiLightOutput
{
    private readonly HueBridgeClient _client;
    private readonly string _groupId;
    private bool _connected;
    private IReadOnlyList<LightDescriptor>? _cachedLights;

    public HueRestLightOutput(HueBridgeClient client, string groupId)
    {
        _client = client;
        _groupId = groupId;
    }

    public bool IsConnected => _connected && _client.IsPaired;
    public bool UsesStateEventAttribution => true;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (!_client.IsPaired)
        {
            throw new InvalidOperationException(
                "HueBridgeClient is not paired ; pair it before constructing a HueRestLightOutput.");
        }
        _connected = true;
        _cachedLights = null;
        return Task.CompletedTask;
    }

    public Task SetColorAsync(LightColor color, CancellationToken ct = default)
        => _client.SetGroupColorAsync(_groupId, color, ct);

    public async Task<IReadOnlyList<LightDescriptor>> ListLightsAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "HueRestLightOutput is not connected ; call ConnectAsync first.");
        }

        if (_cachedLights is not null) return _cachedLights;

        var hueLights = await _client.ListLightsInGroupAsync(_groupId, ct).ConfigureAwait(false);

        var descriptors = new LightDescriptor[hueLights.Count];
        for (int i = 0; i < hueLights.Count; i++)
        {
            var l = hueLights[i];
            descriptors[i] = new LightDescriptor(l.Id, l.Name, l.Reachable);
        }
        _cachedLights = descriptors;
        return _cachedLights;
    }

    public Task SetLightColorsAsync(IReadOnlyDictionary<string, LightColor> colorsByLightId, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "HueRestLightOutput is not connected ; call ConnectAsync first.");
        }
        if (colorsByLightId.Count == 0) return Task.CompletedTask;

        var tasks = new Task[colorsByLightId.Count];
        int i = 0;
        foreach (var (lightId, color) in colorsByLightId)
        {
            tasks[i++] = _client.SetLightColorAsync(lightId, color, ct);
        }
        return Task.WhenAll(tasks);
    }

    public Task IdentifyLightAsync(string lightId, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "HueRestLightOutput is not connected ; call ConnectAsync first.");
        }
        return _client.IdentifyLightAsync(lightId, ct);
    }

    public Task StopIdentifyAsync(string lightId, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "HueRestLightOutput is not connected ; call ConnectAsync first.");
        }
        return _client.StopIdentifyAsync(lightId, ct);
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        _cachedLights = null;
        return ValueTask.CompletedTask;
    }
}
