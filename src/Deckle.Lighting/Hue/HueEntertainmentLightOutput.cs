using System.Diagnostics;

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

    // How long the restore keeps watching after its first write. With the
    // area's "When syncing stops" option on "Last on-state" (the Hue app's
    // default), the bridge writes its own post-stream state some time after
    // action=stop: about 1.7 s in Glowcast's measurements, 0.3 to 0.6 s
    // after a client restore in LumaSync's. Three seconds covers both with
    // margin, and no signal says when that write lands, so the window is
    // fixed rather than ended early.
    private static readonly TimeSpan DefaultRestoreWatchWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultRestorePollInterval = TimeSpan.FromMilliseconds(250);
    // A light the person changes in the Hue app during the window would
    // read as drift too; two rewrites per light bound how long Deckle can
    // fight such a change.
    private const int MaxRestoreRewritesPerLight = 2;

    private readonly IHueEntertainmentBridgeClient _client;
    private readonly HueEntertainmentArea _area;
    private readonly IHueEntertainmentTransport _transport;
    private readonly Dictionary<string, List<HueEntertainmentChannel>> _channelsByLightId;
    private readonly TimeSpan _restoreWatchWindow;
    private readonly TimeSpan _restorePollInterval;

    // Each area light's state as read before the pre-prime touched it,
    // written back once streaming ends. Null when nothing was read.
    private IReadOnlyDictionary<string, HueLightState>? _statesBeforeStreaming;
    private bool _connected;
    private byte _sequence = 1;
    private long _sessionStartedAt;
    private long _framesSent;
    private int _sendFailures;

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
        IHueEntertainmentTransport transport,
        TimeSpan? restoreWatchWindow = null,
        TimeSpan? restorePollInterval = null)
    {
        _client = client;
        _area = area;
        _transport = transport;
        _channelsByLightId = BuildChannelMap(area);
        _restoreWatchWindow = restoreWatchWindow ?? DefaultRestoreWatchWindow;
        _restorePollInterval = restorePollInterval ?? DefaultRestorePollInterval;
    }

    public bool IsConnected => _connected;
    public bool RequiresContinuousColorUpdates => true;
    public int? PreferredColorUpdateRateHz => 50;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;
        if (!_client.IsPaired)
            throw new InvalidOperationException("HueBridgeClient is not paired.");

        ct.ThrowIfCancellationRequested();

        long connectStartedAt = Stopwatch.GetTimestamp();
        string stage = "capture_states";
        bool streamingStarted = false;
        try
        {
            _statesBeforeStreaming = await CaptureLightStatesAsync(ct).ConfigureAwait(false);

            stage = "pre_prime";
            await PrePrimeLightsAsync(ct).ConfigureAwait(false);

            DeckleLightingSource.Log.EntertainmentStreamingStarting();
            DeckleLightingSource.Log.EntertainmentStreamingStartingDetail(
                _area.Id, _area.Name, _area.Channels.Count);

            stage = "streaming_start";
            await _client.SetEntertainmentStreamingAsync(_area.Id, active: true, ct).ConfigureAwait(false);
            streamingStarted = true;

            stage = "transport_connect";
            DeckleLightingSource.Log.EntertainmentTransportConnecting();
            await _transport.ConnectAsync(ct).ConfigureAwait(false);

            _connected = true;
            _framesSent = 0;
            _sendFailures = 0;

            stage = "prime";
            Send(BuildAllChannelColors(LightColor.Black));
            DeckleLightingSource.Log.EntertainmentStreamPrimed(_area.Id, _area.Channels.Count);

            _sessionStartedAt = Stopwatch.GetTimestamp();
            DeckleLightingSource.Log.EntertainmentTransportConnected(ElapsedMs(connectStartedAt));
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                DeckleLightingSource.Log.EntertainmentConnectCancelled(stage, ElapsedMs(connectStartedAt));
            else
                DeckleLightingSource.Log.EntertainmentConnectFailed(
                    stage, ex.GetType().Name, ex.Message, ElapsedMs(connectStartedAt));

            _connected = false;
            _transport.Dispose();
            if (streamingStarted)
                await StopStreamingBestEffortAsync().ConfigureAwait(false);
            // The pre-prime may already have dimmed the lights to almost
            // nothing; a start that fails or is cancelled hands them back too.
            await RestoreLightStatesBestEffortAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Why an explicit restore, when the bridge has its own. With the Hue
    // app's default "Last on-state", the bridge returns each light to the
    // state it had when streaming started. Here that state is the
    // pre-prime's near-black, written just before action=start, so the
    // bridge's own return left the room dark (2026-11-07, a dark room in
    // dark mode). With "Pause on the last colour" the bridge returns
    // nothing at all. Reading each light before the pre-prime and writing
    // it back after the stop covers both settings.
    //
    // Alternatives considered. Keeping the last streamed colour hands the
    // person whatever the screen showed last, which is black after a dark
    // scene, the very case that went wrong. A fixed warm white is never
    // dark, but it ignores a room that was off, or set to a scene the
    // person chose, before Ambient started. Dropping the pre-prime would
    // let the bridge's own return work, but brings back Hue's power-on
    // scene during action=start, and still does nothing under "Pause on
    // the last colour".
    private async Task<IReadOnlyDictionary<string, HueLightState>?> CaptureLightStatesAsync(CancellationToken ct)
    {
        var lightIds = BuildDistinctLightIds();
        if (lightIds.Length == 0) return null;

        try
        {
            var all = await _client.GetLightStatesAsync(ct).ConfigureAwait(false);
            var states = new Dictionary<string, HueLightState>(lightIds.Length, StringComparer.Ordinal);
            foreach (var id in lightIds)
            {
                if (all.TryGetValue(id, out var state))
                    states[id] = state;
            }
            DeckleLightingSource.Log.EntertainmentLightStatesCapturedDetail(_area.Id, states.Count, lightIds.Length);
            return states;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Streaming still starts: the lights then end on whatever the
            // bridge does after the stop, as they did before this restore.
            DeckleLightingSource.Log.EntertainmentLightStatesCaptureFailed(ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // Writes every saved state once, then watches the lights for the window
    // and writes again any the bridge's own post-stream write moved away.
    // Never throws: a light that cannot be restored is reported and skipped.
    private async Task RestoreLightStatesBestEffortAsync()
    {
        var saved = _statesBeforeStreaming;
        _statesBeforeStreaming = null;
        if (saved is null || saved.Count == 0) return;

        long restoreStartedAt = Stopwatch.GetTimestamp();
        using var cts = new CancellationTokenSource(_restoreWatchWindow + StopStreamingTimeout);
        var rewrites = new Dictionary<string, int>(saved.Count, StringComparer.Ordinal);
        int corrections = 0;
        var failed = new HashSet<string>(StringComparer.Ordinal);

        await WriteStatesAsync(saved.Keys, saved, failed, cts.Token).ConfigureAwait(false);

        try
        {
            while (Stopwatch.GetElapsedTime(restoreStartedAt) < _restoreWatchWindow)
            {
                await Task.Delay(_restorePollInterval, cts.Token).ConfigureAwait(false);

                var current = await _client.GetLightStatesAsync(cts.Token).ConfigureAwait(false);
                var drifted = new List<string>();
                foreach (var (id, state) in saved)
                {
                    if (failed.Contains(id)) continue;
                    if (!current.TryGetValue(id, out var now) || state.Matches(now)) continue;
                    int count = rewrites.GetValueOrDefault(id);
                    if (count >= MaxRestoreRewritesPerLight) continue;
                    rewrites[id] = count + 1;
                    drifted.Add(id);
                }

                if (drifted.Count == 0) continue;
                corrections += drifted.Count;
                await WriteStatesAsync(drifted, saved, failed, cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // The first write already went out; losing the watch only means
            // a late post-stream write from the bridge is not corrected.
            DeckleLightingSource.Log.HueStepFailedDetail("restore_watch", ex.GetType().Name, ex.Message);
        }

        DeckleLightingSource.Log.EntertainmentLightStatesRestored(
            saved.Count - failed.Count,
            corrections,
            failed.Count,
            ElapsedMs(restoreStartedAt));
    }

    private async Task WriteStatesAsync(
        IEnumerable<string> lightIds,
        IReadOnlyDictionary<string, HueLightState> saved,
        HashSet<string> failed,
        CancellationToken ct)
    {
        var ids = lightIds.ToArray();
        var writes = new Task[ids.Length];
        for (int i = 0; i < ids.Length; i++)
            writes[i] = _client.SetLightStateAsync(ids[i], saved[ids[i]], ct);

        for (int i = 0; i < ids.Length; i++)
        {
            try
            {
                await writes[i].ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (failed.Add(ids[i]))
                    DeckleLightingSource.Log.EntertainmentLightRestoreFailed(ids[i], ex.GetType().Name, ex.Message);
            }
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
            DeckleLightingSource.Log.EntertainmentPrePrimeFailed(ex.GetType().Name, ex.Message);
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
        bool stopped = await StopStreamingBestEffortAsync().ConfigureAwait(false);
        DeckleLightingSource.Log.EntertainmentSessionClosed(
            stopped ? "completed" : "stop_failed",
            ElapsedMs(_sessionStartedAt),
            _framesSent,
            _sendFailures);

        // After the stop: while the area streams, the bridge ignores REST
        // writes to its lights. The engine awaits this disposal before a new
        // start, so a quick off-on waits out the watch window instead of
        // racing it.
        await RestoreLightStatesBestEffortAsync().ConfigureAwait(false);
    }

    private void Send(IReadOnlyList<HueEntertainmentChannelColor> colors)
    {
        var frames = HueEntertainmentFrameBuilder.BuildFrames(_area.Id, colors, _sequence);
        _sequence = unchecked((byte)(_sequence + frames.Count));
        foreach (var frame in frames)
        {
            try
            {
                _transport.Send(frame);
            }
            catch
            {
                // Counted for the session summary only; the caller's
                // push-health episode reports the failure itself.
                _sendFailures++;
                throw;
            }
            _framesSent++;
        }
    }

    private static long ElapsedMs(long startedAt)
        => startedAt == 0 ? 0 : (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

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

    private async Task<bool> StopStreamingBestEffortAsync()
    {
        try
        {
            using var stopCts = new CancellationTokenSource(StopStreamingTimeout);
            await _client.SetEntertainmentStreamingAsync(_area.Id, active: false, stopCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort cleanup. A failed stop must not hide the
            // original connect/push/dispose failure path.
            DeckleLightingSource.Log.EntertainmentStreamingStopFailed(ex.GetType().Name, ex.Message);
            return false;
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
    Task<IReadOnlyDictionary<string, HueLightState>> GetLightStatesAsync(CancellationToken ct = default);
    Task SetLightStateAsync(string lightId, HueLightState state, CancellationToken ct = default);
    Task IdentifyLightAsync(string lightId, CancellationToken ct = default);
    Task StopIdentifyAsync(string lightId, CancellationToken ct = default);
}

internal interface IHueEntertainmentTransport : IDisposable
{
    Task ConnectAsync(CancellationToken ct);
    void Send(byte[] payload);
}
