using System.Text.Json;

namespace Deckle.Lighting.Hue;

public sealed partial class HueBridgeClient
{
    /// <summary>
    /// Long-running consumer of the bridge's v2 EventStream (SSE). Yields
    /// resource updates as they happen so callers can detect external
    /// state changes (Hue app, Home Assistant, physical Dimmer Switch).
    /// The method reconnects with a 2 s backoff on any
    /// network/parsing error ; only cancellation stops the loop.
    /// </summary>
    /// <remarks>
    /// The bridge echoes our own REST PUTs back as events. Discrimination
    /// is the caller's responsibility (compare event timestamp with the
    /// last self-push for the same resource).
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

        DeckleLightingSource.Log.EventStreamStarting();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await StreamOneConnectionAsync(streamHttp, onUpdate, ct).ConfigureAwait(false);
                DeckleLightingSource.Log.EventStreamReconnecting("clean_close");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                DeckleLightingSource.Log.EventStreamReconnecting($"{ex.GetType().Name}: {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        DeckleLightingSource.Log.EventStreamStopped();
    }

    private async Task StreamOneConnectionAsync(
        HttpClient streamHttp,
        Action<HueResourceUpdate> onUpdate,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "eventstream/clip/v2");
        request.Headers.Add("hue-application-key", _credentials!.Username);
        request.Headers.Add("Accept", "text/event-stream");

        using var response = await streamHttp.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var parser = System.Net.ServerSentEvents.SseParser.Create(stream, (eventType, bytes) =>
        {
            try
            {
                return JsonSerializer.Deserialize<HueEventStreamContainer[]>(bytes, _jsonOptions);
            }
            catch (JsonException)
            {
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
}
