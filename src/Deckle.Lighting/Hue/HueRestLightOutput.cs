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
//
// Hand-back. The output Ambient drives (built by HueLightOutputFactory) reads
// the state of the group's lights on connect and writes it back on dispose,
// the same way the Entertainment output does, so the fallback path does not
// leave the room on the last colour Ambient pushed. An output built with the
// public constructor (light placement) does neither: it
// would write a stale state over whatever the person did meanwhile.
public sealed class HueRestLightOutput : IMultiLightOutput
{
    private readonly IHueRestBridgeClient _client;
    private readonly string _groupId;
    private readonly HueLightStateRestorer? _restorer;
    private IReadOnlyDictionary<string, HueLightState>? _inheritedStates;
    private IReadOnlyDictionary<string, HueLightState>? _statesBeforeAmbient;
    private bool _connected;
    private IReadOnlyList<LightDescriptor>? _cachedLights;

    public HueRestLightOutput(HueBridgeClient client, string groupId)
        : this(client, groupId, restoreStates: false)
    {
    }

    internal HueRestLightOutput(
        IHueRestBridgeClient client,
        string groupId,
        bool restoreStates,
        TimeSpan? restoreWatchWindow = null)
    {
        _client = client;
        _groupId = groupId;
        if (restoreStates)
        {
            // No watch window by default: the bridge's own post-stream write
            // belongs to Entertainment; REST has nothing to undo the restore.
            _restorer = new HueLightStateRestorer(
                client,
                restoreWatchWindow ?? TimeSpan.Zero,
                HueLightStateRestorer.DefaultPollInterval);
        }
    }

    // States an Entertainment start captured before it touched any light and
    // then failed on. Used in place of a fresh read for those lights, which
    // that start may already have dimmed.
    internal void InheritStatesBeforeStreaming(IReadOnlyDictionary<string, HueLightState>? states)
        => _inheritedStates = states;

    public bool IsConnected => _connected && _client.IsPaired;
    public bool UsesStateEventAttribution => true;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (!_client.IsPaired)
        {
            throw new InvalidOperationException(
                "HueBridgeClient is not paired ; pair it before constructing a HueRestLightOutput.");
        }

        if (_restorer is not null && _statesBeforeAmbient is null)
            _statesBeforeAmbient = await CaptureStatesAsync(ct).ConfigureAwait(false);

        _connected = true;
        _cachedLights = null;
    }

    private async Task<IReadOnlyDictionary<string, HueLightState>?> CaptureStatesAsync(CancellationToken ct)
    {
        var lightIds = new List<string>();
        try
        {
            var groupLights = await _client.ListLightsInGroupAsync(_groupId, ct).ConfigureAwait(false);
            foreach (var light in groupLights) lightIds.Add(light.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DeckleLightingSource.Log.EntertainmentLightStatesCaptureFailed(ex.GetType().Name, ex.Message);
        }

        var captured = await _restorer!.CaptureAsync(_groupId, lightIds, ct).ConfigureAwait(false);
        if (_inheritedStates is null) return captured;

        // A light the failed Entertainment start already read keeps that
        // reading: it is the state from before any light was touched.
        var merged = captured is null
            ? new Dictionary<string, HueLightState>(StringComparer.Ordinal)
            : new Dictionary<string, HueLightState>(captured, StringComparer.Ordinal);
        foreach (var (id, state) in _inheritedStates) merged[id] = state;
        return merged;
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

    public async ValueTask DisposeAsync()
    {
        _connected = false;
        _cachedLights = null;

        var saved = _statesBeforeAmbient;
        _statesBeforeAmbient = null;
        if (_restorer is not null)
            await _restorer.RestoreBestEffortAsync(saved).ConfigureAwait(false);
    }
}

internal interface IHueRestBridgeClient : IHueEntertainmentBridgeClient
{
    Task SetGroupColorAsync(string groupId, LightColor color, CancellationToken ct = default);
    Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct = default);
}
