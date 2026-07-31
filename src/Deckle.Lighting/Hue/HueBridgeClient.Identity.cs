using System.Net.Http.Json;

namespace Deckle.Lighting;

public sealed partial class HueBridgeClient
{
    /// <summary>
    /// Reads the bridge's canonical hardware identity from the unauthenticated
    /// CLIP configuration endpoint. This verifies that a cached LAN address
    /// still belongs to a Hue bridge before stored credentials are used.
    /// </summary>
    public async Task<string> GetBridgeIdAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var config = await _http.GetFromJsonAsync<HueConfigDto>("api/config", _jsonOptions, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(config?.BridgeId))
        {
            throw new InvalidDataException("Hue bridge configuration did not contain a bridge id.");
        }

        return config.BridgeId.Trim();
    }
}
