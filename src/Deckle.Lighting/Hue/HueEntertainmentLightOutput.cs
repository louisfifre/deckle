namespace Deckle.Lighting;

// IMultiLightOutput backed by the Hue Entertainment v2 streaming path:
// CLIP v2 starts/stops the entertainment configuration, then colour
// frames are sent through DTLS-PSK over UDP 2100. The class translates
// Deckle's driver-neutral light ids to HueStream channel ids; Ambient
// never sees the channel model.
internal sealed class HueEntertainmentLightOutput : IMultiLightOutput
{
    private static readonly TimeSpan StopStreamingTimeout = TimeSpan.FromSeconds(3);
    private static readonly LightColor RestPrePrimeColor = new(1, 1, 1);

    private readonly IHueEntertainmentBridgeClient _client;
    private readonly HueEntertainmentArea _area;
    private readonly IHueEntertainmentTransport _transport;
    private readonly Dictionary<string, List<HueEntertainmentChannel>> _channelsByLightId;

    private bool _connected;
    private byte _sequence = 1;

    public HueEntertainmentLightOutput(
        HueBridgeClient client,
        HueEntertainmentArea area,
        string clientKey)
        : this(
            client,
            area,
            new HueEntertainmentTransport(
                client.Bridge.InternalIpAddress,
                client.Credentials?.Username ?? throw new InvalidOperationException("HueBridgeClient is not paired."),
                clientKey))
    {
    }

    internal HueEntertainmentLightOutput(
        IHueEntertainmentBridgeClient client,
        HueEntertainmentArea area,
        IHueEntertainmentTransport transport)
    {
        _client = client;
        _area = area;
        _transport = transport;
        _channelsByLightId = BuildChannelMap(area);
    }

    public bool IsConnected => _connected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;
        if (!_client.IsPaired)
            throw new InvalidOperationException("HueBridgeClient is not paired.");

        ct.ThrowIfCancellationRequested();

        bool streamingStarted = false;
        try
        {
            await PrePrimeLightsAsync(ct).ConfigureAwait(false);

            DeckleLightingSource.Log.EntertainmentStreamingStarting();
            DeckleLightingSource.Log.EntertainmentStreamingStartingDetail(
                _area.Id, _area.Name, _area.Channels.Count);

            await _client.SetEntertainmentStreamingAsync(_area.Id, active: true, ct).ConfigureAwait(false);
            streamingStarted = true;

            DeckleLightingSource.Log.EntertainmentTransportConnecting();
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
            DeckleLightingSource.Log.EntertainmentTransportConnected();

            _connected = true;

            Send(BuildAllChannelColors(LightColor.Black));
            DeckleLightingSource.Log.EntertainmentStreamPrimed(_area.Id, _area.Channels.Count);
        }
        catch
        {
            _connected = false;
            _transport.Dispose();
            if (streamingStarted)
                await StopStreamingBestEffortAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task PrePrimeLightsAsync(CancellationToken ct)
    {
        var lightIds = BuildDistinctLightIds();
        if (lightIds.Length == 0) return;

        DeckleLightingSource.Log.EntertainmentPrePrimeStarting();
        DeckleLightingSource.Log.EntertainmentPrePrimeDetail(_area.Id, lightIds.Length);

        try
        {
            var tasks = new Task[lightIds.Length];
            for (int i = 0; i < lightIds.Length; i++)
            {
                // REST black maps to on:false. Keeping bulbs barely on
                // avoids Hue's own power-on scene during action=start.
                tasks[i] = _client.SetLightColorAsync(lightIds[i], RestPrePrimeColor, ct);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DeckleLightingSource.Log.EntertainmentPrePrimeFailed();
            DeckleLightingSource.Log.EntertainmentPrePrimeFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    public Task SetColorAsync(LightColor color, CancellationToken ct = default)
    {
        EnsureConnected();

        Send(BuildAllChannelColors(color));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LightDescriptor>> ListLightsAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        var lights = new List<LightDescriptor>(_channelsByLightId.Count);
        foreach (var (lightId, channels) in _channelsByLightId)
        {
            string name = channels.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Name))?.Name
                ?? $"Light {lightId}";
            lights.Add(new LightDescriptor(lightId, name, IsReachable: true));
        }
        return Task.FromResult<IReadOnlyList<LightDescriptor>>(lights);
    }

    public Task SetLightColorsAsync(
        IReadOnlyDictionary<string, LightColor> colorsByLightId,
        CancellationToken ct = default)
    {
        EnsureConnected();
        if (colorsByLightId.Count == 0) return Task.CompletedTask;

        var channelColors = new List<HueEntertainmentChannelColor>(colorsByLightId.Count);
        foreach (var (lightId, color) in colorsByLightId)
        {
            if (!_channelsByLightId.TryGetValue(lightId, out var channels))
                continue;

            foreach (var channel in channels)
                channelColors.Add(new HueEntertainmentChannelColor(channel.ChannelId, color));
        }

        if (channelColors.Count > 0)
            Send(channelColors);
        return Task.CompletedTask;
    }

    public Task IdentifyLightAsync(string lightId, CancellationToken ct = default)
        => _client.IdentifyLightAsync(lightId, ct);

    public Task StopIdentifyAsync(string lightId, CancellationToken ct = default)
        => _client.StopIdentifyAsync(lightId, ct);

    public async ValueTask DisposeAsync()
    {
        if (!_connected)
        {
            _transport.Dispose();
            return;
        }

        _connected = false;
        _transport.Dispose();
        await StopStreamingBestEffortAsync().ConfigureAwait(false);
    }

    private void Send(IReadOnlyList<HueEntertainmentChannelColor> colors)
    {
        var frames = HueEntertainmentFrameBuilder.BuildFrames(_area.Id, colors, _sequence);
        _sequence = unchecked((byte)(_sequence + frames.Count));
        foreach (var frame in frames)
            _transport.Send(frame);
    }

    private HueEntertainmentChannelColor[] BuildAllChannelColors(LightColor color)
    {
        var colors = new HueEntertainmentChannelColor[_area.Channels.Count];
        for (int i = 0; i < _area.Channels.Count; i++)
            colors[i] = new HueEntertainmentChannelColor(_area.Channels[i].ChannelId, color);
        return colors;
    }

    private string[] BuildDistinctLightIds()
    {
        var lightIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in _area.Channels)
        {
            if (!string.IsNullOrWhiteSpace(channel.LightId))
                lightIds.Add(channel.LightId);
        }
        return lightIds.ToArray();
    }

    private async Task StopStreamingBestEffortAsync()
    {
        try
        {
            using var stopCts = new CancellationTokenSource(StopStreamingTimeout);
            await _client.SetEntertainmentStreamingAsync(_area.Id, active: false, stopCts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup. A failed stop must not hide the
            // original connect/push/dispose failure path.
        }
    }

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException("HueEntertainmentLightOutput is not connected; call ConnectAsync first.");
    }

    private static Dictionary<string, List<HueEntertainmentChannel>> BuildChannelMap(HueEntertainmentArea area)
    {
        var map = new Dictionary<string, List<HueEntertainmentChannel>>(StringComparer.Ordinal);
        foreach (var channel in area.Channels)
        {
            if (string.IsNullOrWhiteSpace(channel.LightId)) continue;
            if (!map.TryGetValue(channel.LightId, out var channels))
            {
                channels = [];
                map[channel.LightId] = channels;
            }
            channels.Add(channel);
        }
        return map;
    }
}

internal interface IHueEntertainmentBridgeClient
{
    bool IsPaired { get; }
    Task SetEntertainmentStreamingAsync(string entertainmentConfigurationId, bool active, CancellationToken ct = default);
    Task SetLightColorAsync(string lightId, LightColor color, CancellationToken ct = default);
    Task IdentifyLightAsync(string lightId, CancellationToken ct = default);
    Task StopIdentifyAsync(string lightId, CancellationToken ct = default);
}

internal interface IHueEntertainmentTransport : IDisposable
{
    Task ConnectAsync(CancellationToken ct);
    void Send(byte[] payload);
}
