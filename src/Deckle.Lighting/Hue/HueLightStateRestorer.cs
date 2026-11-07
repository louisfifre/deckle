using System.Diagnostics;

namespace Deckle.Lighting;

// Reads the state of a set of Hue lights before Ambient touches them and
// writes it back when Ambient lets go. Shared by every Hue output, so the
// Entertainment stream and the REST fallback hand the lights back the same
// way (2026-11-07: after Ambient stopped, the lights stayed almost black).
//
// The watch window exists for the Entertainment bridge, which writes its
// own post-stream state some time after action=stop and would otherwise
// undo the restore. With "Last on-state" (the Hue app's default) that write
// lands about 1.7 s after the stop in Glowcast's measurements, 0.3 to 0.6 s
// after a client restore in LumaSync's. Three seconds covers both with
// margin, and no signal says when it lands, so the window is fixed rather
// than ended early. The REST path has no such write and uses a zero window:
// one write, no polling.
internal sealed class HueLightStateRestorer
{
    internal static readonly TimeSpan DefaultWatchWindow = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(3);

    // A light the person changes in the Hue app during the window would
    // read as drift too; two rewrites per light bound how long Deckle can
    // fight such a change.
    private const int MaxRewritesPerLight = 2;

    private readonly IHueLightStateClient _client;
    private readonly TimeSpan _watchWindow;
    private readonly TimeSpan _pollInterval;

    public HueLightStateRestorer(IHueLightStateClient client, TimeSpan watchWindow, TimeSpan pollInterval)
    {
        _client = client;
        _watchWindow = watchWindow;
        _pollInterval = pollInterval;
    }

    // Best effort: null when the bridge cannot be read, so the output still
    // starts and the lights end on whatever the bridge leaves them, as they
    // did before this restore existed. Cancellation still propagates.
    //
    // Keeping the last streamed colour, or a fixed warm white, was rejected:
    // the first hands the person black after a dark scene, the very case that
    // went wrong; the second ignores a room that was off, or on a scene the
    // person chose, before Ambient started.
    public async Task<IReadOnlyDictionary<string, HueLightState>?> CaptureAsync(
        string scopeId,
        IReadOnlyCollection<string> lightIds,
        CancellationToken ct)
    {
        if (lightIds.Count == 0) return null;

        try
        {
            var all = await _client.GetLightStatesAsync(ct).ConfigureAwait(false);
            var states = new Dictionary<string, HueLightState>(lightIds.Count, StringComparer.Ordinal);
            foreach (var id in lightIds)
            {
                if (all.TryGetValue(id, out var state))
                    states[id] = state;
            }
            DeckleLightingSource.Log.EntertainmentLightStatesCapturedDetail(scopeId, states.Count, lightIds.Count);
            return states;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DeckleLightingSource.Log.EntertainmentLightStatesCaptureFailed(ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // Writes every saved state once, then watches the lights for the window
    // and writes again any the bridge's own post-stream write moved away.
    // Never throws: a light that cannot be restored is reported and skipped.
    public async Task RestoreBestEffortAsync(IReadOnlyDictionary<string, HueLightState>? saved)
    {
        if (saved is null || saved.Count == 0) return;

        long startedAt = Stopwatch.GetTimestamp();
        using var cts = new CancellationTokenSource(_watchWindow + WriteTimeout);
        var rewrites = new Dictionary<string, int>(saved.Count, StringComparer.Ordinal);
        int corrections = 0;
        var failed = new HashSet<string>(StringComparer.Ordinal);

        await WriteStatesAsync(saved.Keys, saved, failed, cts.Token).ConfigureAwait(false);

        try
        {
            while (Stopwatch.GetElapsedTime(startedAt) < _watchWindow)
            {
                await Task.Delay(_pollInterval, cts.Token).ConfigureAwait(false);

                var current = await _client.GetLightStatesAsync(cts.Token).ConfigureAwait(false);
                var drifted = new List<string>();
                foreach (var (id, state) in saved)
                {
                    if (failed.Contains(id)) continue;
                    if (!current.TryGetValue(id, out var now) || state.Matches(now)) continue;
                    int count = rewrites.GetValueOrDefault(id);
                    if (count >= MaxRewritesPerLight) continue;
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
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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
}

// What the restorer needs from a bridge: read every light, write one back.
internal interface IHueLightStateClient
{
    Task<IReadOnlyDictionary<string, HueLightState>> GetLightStatesAsync(CancellationToken ct = default);
    Task SetLightStateAsync(string lightId, HueLightState state, CancellationToken ct = default);
}
