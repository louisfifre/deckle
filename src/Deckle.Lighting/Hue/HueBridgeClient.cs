using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Lighting;

// Per-bridge client. One instance is bound to one HueBridge for the
// entire pairing + control lifecycle.
//
// The Hue bridge serves HTTPS on port 443 with a self-signed
// certificate rooted on its own private CA. We accept any certificate
// presented by the bridge IP we explicitly asked for — MITM risk
// exists in theory on the LAN but is comparable to any other LAN
// service the user trusts implicitly. The accept-all callback's scope
// is bounded by construction : the constructor rejects any bridge IP
// outside the private LAN ranges (see IsPrivateBridgeIp), so the
// trusted certificate can only ever come from an RFC1918 / APIPA host,
// never a public address.
//
// Pairing, groups, lights, colour pushes and identify use CLIP v1 (the
// username in the URL path). The Entertainment catalog and streaming
// switch, the EventStream and the v1 ↔ v2 id maps use CLIP v2 (the
// `hue-application-key` header).
public sealed partial class HueBridgeClient : IDisposable, IHueEntertainmentBridgeClient
{
    // Hue caps the `devicetype` string at 40 chars total. "deckle#" is
    // 7 chars, so we have 33 left for the machine name suffix.
    private const string DeviceTypePrefix = "deckle#";
    private const int    DeviceTypeMaxSuffixLength = 33;

    private readonly HueBridge _bridge;
    private readonly HttpClient _http;
    private HueCredentials? _credentials;
    private bool _disposed;

    public HueBridgeClient(HueBridge bridge)
    {
        ValidateBridge(bridge);
        _bridge = bridge;
        _http = CreateBridgeHttpClient(bridge.InternalIpAddress, bridge.Port);
    }

    internal HueBridgeClient(HueBridge bridge, HttpClient http)
    {
        ValidateBridge(bridge);
        _bridge = bridge;
        _http = http;
    }

    /// <summary>
    /// Restores a paired client from persisted credentials, with no new
    /// link-button press: the bridge keeps the username valid until it is
    /// revoked from the Hue app. ClientKey may be empty, since only
    /// Entertainment streaming needs it; callers restore it from the
    /// secret vault when available.
    /// </summary>
    public HueBridgeClient(HueBridge bridge, HueCredentials credentials)
        : this(bridge)
    {
        _credentials = credentials;
    }

    internal HueBridgeClient(HueBridge bridge, HueCredentials credentials, HttpClient http)
        : this(bridge, http)
    {
        _credentials = credentials;
    }

    public HueBridge Bridge => _bridge;

    /// <summary>Null until a pairing succeeds or credentials are restored.</summary>
    public HueCredentials? Credentials => _credentials;

    public bool IsPaired => _credentials is not null;

    private static void ValidateBridge(HueBridge bridge)
    {
        if (!IsPrivateBridgeIp(bridge.InternalIpAddress))
        {
            throw new ArgumentException(
                $"Hue bridge IP '{bridge.InternalIpAddress}' is not on a private LAN range " +
                "(RFC1918 or 169.254/16) — the bridge is a local device and any other " +
                "address is rejected to avoid SSRF.",
                nameof(bridge));
        }
    }

    public async Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        DeckleLightingSource.Log.ListingGroups();

        var dict = await _http.GetFromJsonAsync<Dictionary<string, HueGroupDto>>(
            $"api/{_credentials!.Username}/groups", _jsonOptions, ct)
            .ConfigureAwait(false);

        if (dict is null)
        {
            DeckleLightingSource.Log.BridgeReturnedNoGroups();
            return [];
        }

        var groups = new List<HueGroup>(dict.Count);
        foreach (var (id, dto) in dict)
        {
            int lightsCount = dto.Lights?.Length ?? 0;
            groups.Add(new HueGroup(id, dto.Name ?? "", dto.Type ?? "Unknown", lightsCount));
        }

        DeckleLightingSource.Log.GroupsListed(_bridge.Id, groups.Count);
        foreach (var g in groups)
        {
            DeckleLightingSource.Log.GroupListed(g.Id, g.Name, g.Type, g.LightsCount);
        }
        return groups;
    }

    // Hue's `transitiontime` is the bridge-side fade toward the new state,
    // in deciseconds. The factory default makes the lamp lag the screen.
    // Smoothing happens client-side, so the shortest non-zero fade keeps
    // the lamp responsive while successive push ticks still blend instead
    // of reading as strobing.
    private const int AmbientTransitionDeciseconds = 1;

    /// <summary>
    /// Pairing must have completed. Pure black is sent as <c>on:false</c>
    /// so the lamp goes off instead of jumping to the nearest in-gamut
    /// colour.
    /// </summary>
    public Task SetGroupColorAsync(string groupId, LightColor color, CancellationToken ct = default)
        => PutStateAsync(
            $"api/{_credentials!.Username}/groups/{groupId}/action",
            color,
            target: $"group_id={groupId}",
            ct);

    public Task SetLightColorAsync(string lightId, LightColor color, CancellationToken ct = default)
        => PutStateAsync(
            $"api/{_credentials!.Username}/lights/{lightId}/state",
            color,
            target: $"light_id={lightId}",
            ct);

    private async Task PutStateAsync(string url, LightColor color, string target, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        var (xy, bri) = HueColorMath.RgbToHueXyBri(color);
        var body = new HueStateRequest
        {
            TransitionTime = AmbientTransitionDeciseconds,
        };

        if (bri == 0)
        {
            // The bridge ignores xy and bri alongside on:false;
            // transitiontime still applies, so the light fades out.
            body.On = false;
        }
        else
        {
            body.On = true;
            body.Brightness = bri;
            body.Xy = new[] { xy.X, xy.Y };
        }

        var response = await _http.PutAsJsonAsync(url, body, _jsonOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            DeckleLightingSource.Log.SetColorFailed(target, (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        if (bri == 0)
        {
            DeckleLightingSource.Log.PushColorOff(
                target, color.R, color.G, color.B, AmbientTransitionDeciseconds);
        }
        else
        {
            DeckleLightingSource.Log.PushColor(
                target, color.R, color.G, color.B, xy.X, xy.Y, bri, AmbientTransitionDeciseconds);
        }
    }

    /// <summary>
    /// Flashes the light so the user can spot it. <c>alert=lselect</c>
    /// pulses for about 15 s, then the bridge restores the previous state
    /// itself; the PUT is acknowledged at once. <see cref="StopIdentifyAsync"/>
    /// cuts the flash short.
    /// </summary>
    public Task IdentifyLightAsync(string lightId, CancellationToken ct = default)
        => SendAlertAsync(lightId, "lselect", "start", ct);

    /// <summary>
    /// Sends <c>alert=none</c>. Harmless on a light that is not flashing;
    /// the bridge restores the pre-flash state.
    /// </summary>
    public Task StopIdentifyAsync(string lightId, CancellationToken ct = default)
        => SendAlertAsync(lightId, "none", "stop", ct);

    private async Task SendAlertAsync(string lightId, string alert, string phase, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        var body = new HueAlertRequest { Alert = alert };

        var response = await _http.PutAsJsonAsync(
            $"api/{_credentials!.Username}/lights/{lightId}/state",
            body, _jsonOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            DeckleLightingSource.Log.IdentifyFailed(phase, lightId, (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        DeckleLightingSource.Log.LightIdentified(lightId, alert, phase);
    }

    /// <summary>
    /// Lists the group's lights in the bridge's own order. The CLIP v1
    /// group endpoint returns only light ids, so a second GET on
    /// <c>/lights</c> maps each id to its name and reachability.
    /// </summary>
    public async Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        DeckleLightingSource.Log.ListingLightsInGroup();
        DeckleLightingSource.Log.ListingLightsInGroupDetail(groupId);

        var groupDto = await _http.GetFromJsonAsync<HueGroupDto>(
            $"api/{_credentials!.Username}/groups/{groupId}", _jsonOptions, ct)
            .ConfigureAwait(false);

        if (groupDto?.Lights is null || groupDto.Lights.Length == 0)
        {
            DeckleLightingSource.Log.LightsListedEmpty(groupId);
            return [];
        }

        var lightsDict = await _http.GetFromJsonAsync<Dictionary<string, HueLightDto>>(
            $"api/{_credentials!.Username}/lights", _jsonOptions, ct)
            .ConfigureAwait(false);

        if (lightsDict is null)
        {
            DeckleLightingSource.Log.BridgeReturnedNoLights();
            DeckleLightingSource.Log.BridgeReturnedNoLightsDetail(groupId);
            return [];
        }

        var result = new List<HueLight>(groupDto.Lights.Length);
        foreach (var id in groupDto.Lights)
        {
            if (lightsDict.TryGetValue(id, out var dto))
            {
                result.Add(new HueLight(
                    id,
                    dto.Name ?? $"Light {id}",
                    dto.Type ?? "",
                    dto.State?.Reachable ?? true));
            }
            else
            {
                // A group id missing from /lights is kept with a synthetic
                // name, unreachable, so the UI surfaces the discrepancy.
                result.Add(new HueLight(id, $"Light {id}", "", false));
            }
        }

        DeckleLightingSource.Log.LightsListed(groupId, result.Count);
        foreach (var l in result)
        {
            DeckleLightingSource.Log.LightListed(l.Id, l.Name, l.Type, l.Reachable);
        }
        return result;
    }

    private void EnsurePaired()
    {
        if (_credentials is null)
        {
            throw new InvalidOperationException(
                "Bridge is not paired. Call PairAsync first.");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    // RFC1918 + APIPA validation for a Hue bridge address, the single home
    // for this rule. The bridge is a LAN-only device ; accepting an
    // arbitrary IP would let any untrusted source (manual entry, cloud
    // discovery, a corrupted or tampered settings.json) point a client at
    // an attacker-controlled server on the internet (SSRF / data exfil
    // through the PUT-state payload). Both constructors call it before any
    // network call; the ambient engine re-checks the persisted IP at start
    // for an early, friendly message.
    public static bool IsPrivateBridgeIp(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!IPAddress.TryParse(s, out var ip)) return false;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;

        var b = ip.GetAddressBytes();
        return
            b[0] == 10                                          // 10.0.0.0/8     class A private
         || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)           // 172.16.0.0/12  class B private
         || (b[0] == 192 && b[1] == 168)                        // 192.168.0.0/16 class C private
         || (b[0] == 169 && b[1] == 254);                       // 169.254.0.0/16 APIPA link-local
    }

    internal static HttpClient CreateBridgeHttpClient(string ip, int port, TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                // Self-signed bridge certificate: see the class header for
                // why accept-all stays bounded to private LAN addresses.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };

        // HTTPS only: v2 bridge firmware forces it, and bridges that still
        // serve plain HTTP on port 80 are not supported.
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{ip}:{port}/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };
    }

    private static string BuildDeviceType(string machineName)
    {
        // Hue rejects spaces and many punctuation chars in the suffix.
        Span<char> buffer = stackalloc char[DeviceTypeMaxSuffixLength];
        int length = 0;
        foreach (char c in machineName)
        {
            if (length >= buffer.Length) break;
            buffer[length++] = char.IsLetterOrDigit(c) || c == '-' ? c : '-';
        }
        var suffix = length == 0 ? "host" : new string(buffer[..length]);
        return DeviceTypePrefix + suffix;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }

}

// Bridge-side rejection of a pairing attempt for a reason other than
// "link button not pressed".
public sealed class HuePairingException : Exception
{
    public HuePairingException(string message) : base(message) { }
    public HuePairingException(string message, Exception inner) : base(message, inner) { }
}

// Transport-level failure : the bridge IP doesn't answer on TCP, the
// TLS handshake fails, or the DNS lookup fails. Distinct from
// HuePairingException so the UI can surface "check that the bridge is
// powered on and reachable" instead of "the bridge refused".
public sealed class HueBridgeUnreachableException : Exception
{
    public HueBridgeUnreachableException(string message) : base(message) { }
    public HueBridgeUnreachableException(string message, Exception inner) : base(message, inner) { }
}
