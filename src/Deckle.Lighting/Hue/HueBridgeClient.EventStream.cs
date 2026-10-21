using System.Text.Json;

namespace Deckle.Lighting;

public sealed partial class HueBridgeClient
{
    /// <summary>
    /// Long-running consumer of the bridge's v2 EventStream (SSE), so
    /// callers can attribute bridge-side state changes (Hue app, Home
    /// Assistant, a physical switch, or echoes of our own calls).
    /// Reconnects after any network or parsing error; only cancellation
    /// stops the loop.
    /// </summary>
    /// <remarks>
    /// The bridge echoes our own REST PUTs back as events. Discrimination
    /// is the caller's responsibility (compare the event with its own
    /// pending push state for the same resource).
    /// </remarks>
    public async Task StreamEventsAsync(
        Action<HueResourceUpdate> onUpdate,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        // SSE needs a dedicated HttpClient with InfiniteTimeSpan: the shared _http has Timeout = 10 s.
        using var streamHttp = CreateBridgeHttpClient(
            _bridge.InternalIpAddress, _bridge.Port, Timeout.InfiniteTimeSpan);
        var episode = new HueEventStreamEpisode();
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        Exception? failure = null;

        DeckleLightingSource.Log.EventStreamStarting();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await StreamOneConnectionAsync(
                        streamHttp,
                        onUpdate,
                        () => ReportEventStreamRecovery(episode),
                        ct).ConfigureAwait(false);

                    BeginEventStreamLoss(episode, exception: null, ct);
                    DeckleLightingSource.Log.EventStreamReconnecting("clean_close");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    BeginEventStreamLoss(episode, ex, ct);
                    DeckleLightingSource.Log.EventStreamReconnecting("exception");
                }

                try { await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            DeckleLightingSource.Log.EventStreamStopped(
                failure is null ? "cancelled" : "failed",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                failure?.GetType().Name ?? "none",
                failure?.Message ?? "none");
        }
    }

    private async Task StreamOneConnectionAsync(
        HttpClient streamHttp,
        Action<HueResourceUpdate> onUpdate,
        Action onConnected,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "eventstream/clip/v2");
        request.Headers.Add("hue-application-key", _credentials!.Username);
        request.Headers.Add("Accept", "text/event-stream");

        using var response = await streamHttp.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        onConnected();

        var parser = System.Net.ServerSentEvents.SseParser.Create(stream, (eventType, bytes) =>
        {
            try
            {
                return JsonSerializer.Deserialize<HueEventStreamContainer[]>(bytes, _jsonOptions);
            }
            catch (JsonException ex)
            {
                // One malformed event must not drop the connection.
                DeckleLightingSource.Log.HueStepFailedDetail("event_stream_parse", ex.GetType().Name, ex.Message);
                return null;
            }
        });

        await foreach (var item in parser.EnumerateAsync(ct).ConfigureAwait(false))
        {
            if (item.Data is not { } containers) continue;
            foreach (var container in containers)
            {
                if (container.Type != "update" || container.Data is null) continue;
                foreach (var resource in container.Data)
                {
                    if (string.IsNullOrEmpty(resource.Id) || string.IsNullOrEmpty(resource.Type)) continue;
                    onUpdate(new HueResourceUpdate(
                        V2ResourceId: resource.Id,
                        ResourceType: resource.Type,
                        CreationTime: container.CreationTime,
                        On:           resource.On?.On,
                        Brightness:   resource.Bri?.Bri,
                        Xy:           resource.Xy?.Xy is { } xy ? (xy.X, xy.Y) : null));
                }
            }
        }
    }

    private static void BeginEventStreamLoss(
        HueEventStreamEpisode episode,
        Exception? exception,
        CancellationToken ct)
    {
        HueEventStreamLoss loss = episode.RecordLoss(exception);
        if (loss.Started)
        {
            _ = ReportEventStreamIncidentAfterDelayAsync(episode, loss.Generation, ct);
        }
    }

    private static async Task ReportEventStreamIncidentAfterDelayAsync(
        HueEventStreamEpisode episode,
        long generation,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(HueEventStreamEpisode.IncidentDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested
            || !episode.TryOpenIncident(generation, out HueEventStreamObservation observation))
        {
            return;
        }

        Exception? exception = observation.LastException;
        DeckleLightingSource.Log.EventStreamIncident(
            (long)observation.Duration.TotalMilliseconds,
            observation.FailureCount,
            exception is null ? "clean_close" : "exception",
            exception?.GetType().Name ?? "none",
            exception?.Message ?? "none");
    }

    private static void ReportEventStreamRecovery(HueEventStreamEpisode episode)
    {
        if (!episode.TryRecover(out HueEventStreamObservation observation)) return;

        DeckleLightingSource.Log.EventStreamRecovered();
        DeckleLightingSource.Log.EventStreamRecoveryDetail(
            (long)observation.Duration.TotalMilliseconds,
            observation.FailureCount);
    }
}
