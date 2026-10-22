using System.Net.Http.Json;

namespace Deckle.Lighting;

public sealed partial class HueBridgeClient
{
    /// <summary>
    /// Polls until the user presses the link button on the bridge.
    /// Throws <see cref="TimeoutException"/> when no press comes within
    /// <paramref name="overallTimeout"/>.
    /// </summary>
    public async Task<HueCredentials> PairAsync(
        TimeSpan overallTimeout,
        TimeSpan? pollInterval = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        var deadline = DateTime.UtcNow + overallTimeout;
        var deviceType = BuildDeviceType(Environment.MachineName);

        DeckleLightingSource.Log.PairingStarted();
        DeckleLightingSource.Log.PairingStartedDetail(
            _bridge.InternalIpAddress, (int)overallTimeout.TotalSeconds, deviceType);

        int attempts = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            attempts++;
            var outcome = await PairAttemptAsync(deviceType, ct).ConfigureAwait(false);
            switch (outcome)
            {
                case PairOutcome.Success success:
                    _credentials = success.Credentials;
                    DeckleLightingSource.Log.BridgePaired();
                    DeckleLightingSource.Log.BridgePairedDetail2(_bridge.Id);
                    return _credentials;

                case PairOutcome.LinkButtonNotPressed:
                    DeckleLightingSource.Log.PairingWaiting((int)interval.TotalMilliseconds);
                    break;

                case PairOutcome.OtherError otherError:
                    DeckleLightingSource.Log.PairingRejected(otherError.Type, otherError.Description);
                    throw new HuePairingException(
                        $"Bridge refused pairing (type {otherError.Type}): {otherError.Description}");
            }

            await Task.Delay(interval, ct).ConfigureAwait(false);
        }

        DeckleLightingSource.Log.PairingTimedOut(attempts, overallTimeout.TotalSeconds);
        throw new TimeoutException(
            "Hue bridge pairing timed out. Press the link button on the bridge and try again.");
    }

    private async Task<PairOutcome> PairAttemptAsync(string deviceType, CancellationToken ct)
    {
        var body = new HuePairRequest
        {
            DeviceType = deviceType,
            GenerateClientKey = true,
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("api", body, _jsonOptions, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            DeckleLightingSource.Log.BridgeUnreachable(
                ex.GetType().Name,
                ex.Message,
                (ex.InnerException as System.Net.Sockets.SocketException)?.NativeErrorCode ?? 0);
            throw new HueBridgeUnreachableException(
                $"Bridge at {_bridge.InternalIpAddress} is unreachable.", ex);
        }

        // The bridge returns 200 even for "link button not pressed"; the JSON body carries the real result.
        if (!response.IsSuccessStatusCode)
        {
            DeckleLightingSource.Log.PairingHttpError((int)response.StatusCode);
            throw new HttpRequestException(
                $"Hue bridge returned {(int)response.StatusCode} on pairing.");
        }

        var elements = await response.Content
            .ReadFromJsonAsync<HueApiResponseElement[]>(_jsonOptions, ct)
            .ConfigureAwait(false);

        if (elements is null || elements.Length == 0)
            return new PairOutcome.OtherError(-1, "Empty response from bridge.");

        var element = elements[0];
        if (element.Success is { Username.Length: > 0 } success)
        {
            return new PairOutcome.Success(
                new HueCredentials(success.Username, success.ClientKey));
        }

        if (element.Error is { } error)
        {
            return error.Type == 101
                ? new PairOutcome.LinkButtonNotPressed()
                : new PairOutcome.OtherError(error.Type, error.Description);
        }

        return new PairOutcome.OtherError(-1, "Unrecognised response shape.");
    }
}
