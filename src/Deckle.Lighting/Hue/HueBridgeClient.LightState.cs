using System.Net.Http.Json;

namespace Deckle.Lighting;

public sealed partial class HueBridgeClient
{
    // Hue's own default fade, 400 ms: a light handed back by Ambient eases
    // into the state it had before instead of snapping to it.
    private const int RestoreTransitionDeciseconds = 4;

    /// <summary>
    /// Reads every light's current state in one CLIP v1 request, keyed by
    /// light id. A light the bridge reports without an on/off value is left
    /// out, since there is nothing to write back for it.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, HueLightState>> GetLightStatesAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        var lights = await _http.GetFromJsonAsync<Dictionary<string, HueLightDto>>(
            $"api/{_credentials!.Username}/lights", _jsonOptions, ct)
            .ConfigureAwait(false);

        var states = new Dictionary<string, HueLightState>(StringComparer.Ordinal);
        if (lights is null) return states;

        foreach (var (id, dto) in lights)
        {
            if (dto.State is not { On: { } on } s) continue;
            states[id] = new HueLightState(
                on, s.Brightness ?? 0, s.ColorMode, s.Xy, s.Ct, s.Hue, s.Saturation);
        }
        return states;
    }

    /// <summary>
    /// Writes <paramref name="state"/> back to one light: off stays off,
    /// on carries its brightness and the colour fields of its colour mode.
    /// </summary>
    public async Task SetLightStateAsync(string lightId, HueLightState state, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();
        ArgumentNullException.ThrowIfNull(state);

        var body = new HueStateRequest
        {
            On = state.On,
            TransitionTime = RestoreTransitionDeciseconds,
        };
        if (state.On)
        {
            // CLIP v1 brightness runs 1..254 while on.
            body.Brightness = Math.Max((byte)1, state.Brightness);
            switch (state.ColorMode)
            {
                case "xy" when state.Xy is [_, _]:
                    body.Xy = state.Xy;
                    break;
                case "ct" when state.ColorTemperature is { } mired:
                    body.Ct = mired;
                    break;
                case "hs" when state.Hue is { } hue && state.Saturation is { } sat:
                    body.Hue = hue;
                    body.Saturation = sat;
                    break;
            }
        }

        var response = await _http.PutAsJsonAsync(
            $"api/{_credentials!.Username}/lights/{lightId}/state",
            body, _jsonOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            DeckleLightingSource.Log.SetColorFailed($"light_id={lightId}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
    }
}
