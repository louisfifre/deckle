using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Lighting.Hue;

// Per-bridge REST client. One instance is bound to one HueBridge for
// the entire pairing + control lifecycle ; consumers that need to
// talk to several bridges (rare on a home network) instantiate one
// client per bridge.
//
// The Hue bridge serves HTTPS on port 443 with a self-signed
// certificate rooted on its own private CA. We accept any certificate
// presented by the bridge IP we explicitly asked for — MITM risk
// exists in theory on the LAN but is comparable to any other LAN
// service the user trusts implicitly. The accept-all callback's scope
// is bounded by construction : the constructor rejects any bridge IP
// outside the private LAN ranges (see IsPrivateBridgeIp), so the
// trusted certificate can only ever come from an RFC1918 / APIPA host,
// never a public address. The remaining alternative (importing the
// bridge CA into the system store, or pinning the SubjectPublicKeyInfo
// at first pair) is a polish item for a later milestone.
//
// All endpoints used at J2 are CLIP v1 — the older REST surface that
// still works on every v2 bridge, takes the username in the URL path,
// and is simpler to drive than CLIP v2's resource graph. Migration to
// CLIP v2 (request via `hue-application-key` header, resource-oriented
// `/clip/v2/resource/*` paths) can land later behind the same
// HueBridgeClient API.
public sealed partial class HueBridgeClient : IDisposable
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
        // The bridge is a LAN-only device. Reject any address outside
        // the private ranges before we build the HttpClient (and before
        // the accept-all TLS callback can ever fire) so no caller —
        // manual entry, cloud discovery, a tampered settings.json — can
        // point this client at an attacker-controlled host on the
        // internet (SSRF / data exfil through the PUT-state payload).
        if (!IsPrivateBridgeIp(bridge.InternalIpAddress))
        {
            throw new ArgumentException(
                $"Hue bridge IP '{bridge.InternalIpAddress}' is not on a private LAN range " +
                "(RFC1918 or 169.254/16) — the bridge is a local device and any other " +
                "address is rejected to avoid SSRF.",
                nameof(bridge));
        }

        _bridge = bridge;
        _http = CreateBridgeHttpClient(bridge.InternalIpAddress, bridge.Port);
    }

    /// <summary>
    /// Restores a previously-paired client from persisted credentials.
    /// Used at app start to skip the link-button dance when the user has
    /// already paired in a previous session — the bridge keeps the
    /// username valid until manually revoked from the Hue app. The
    /// ClientKey field can be left empty when restoring from persisted
    /// state : the REST CLIP v1 path only uses Username, the PSK is
    /// reserved for Entertainment v2 DTLS (not in scope J3).
    /// </summary>
    public HueBridgeClient(HueBridge bridge, HueCredentials credentials)
        : this(bridge)
    {
        _credentials = credentials;
    }

    /// <summary>The bridge this client targets.</summary>
    public HueBridge Bridge => _bridge;

    /// <summary>Credentials obtained from a successful pairing, or
    /// null if pairing has not run yet (or has failed).</summary>
    public HueCredentials? Credentials => _credentials;

    /// <summary>True once pairing has succeeded and the bridge accepts
    /// authenticated calls.</summary>
    public bool IsPaired => _credentials is not null;

    /// <summary>
    /// Fetches the list of groups (rooms, zones, entertainment areas)
    /// configured on the bridge. The CLIP v1 endpoint returns a flat
    /// dictionary keyed by group id ; we project it to a list of
    /// public <see cref="HueGroup"/> records so the caller doesn't see
    /// the wire DTO. Pairing must have completed first ;
    /// <see cref="InvalidOperationException"/> is thrown otherwise.
    /// </summary>
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

    // Hue's `transitiontime` is the fade duration the bridge interpolates
    // toward the new state, expressed in deciseconds (1/10 s). The
    // factory default is 4 (= 400 ms), which feels sluggish on a fast
    // dark→light transition (the lamp lags visibly behind the screen).
    // For an ambient-light driver we want near-instant push and let the
    // smoothing happen client-side (J5). 1 (= 100 ms) is a sweet spot for
    // V0 : fast enough to feel responsive, slow enough that the per-tick
    // 15 Hz updates don't read as strobing on the lamp.
    private const int AmbientTransitionDeciseconds = 1;

    /// <summary>
    /// Pushes a single sRGB colour to the given group. RGB is
    /// converted to Hue's xy + brightness representation in-house
    /// (see <see cref="HueColorMath"/>) ; pure black is mapped to
    /// `on:false` so the lamp goes off instead of jumping to the
    /// nearest in-gamut colour. The bridge `transitiontime` is forced
    /// to <see cref="AmbientTransitionDeciseconds"/> so the lamp
    /// doesn't lag the screen by the 400 ms factory default. Pairing
    /// must have completed.
    /// </summary>
    public Task SetGroupColorAsync(string groupId, LightColor color, CancellationToken ct = default)
        => PutStateAsync(
            $"api/{_credentials!.Username}/groups/{groupId}/action",
            color,
            target: $"group_id={groupId}",
            ct);

    /// <summary>
    /// Pushes a single sRGB colour to one individual light. Same
    /// conversion + <c>transitiontime</c> semantics as
    /// <see cref="SetGroupColorAsync"/> ; the difference is the endpoint
    /// (<c>/lights/{id}/state</c> vs. <c>/groups/{id}/action</c>) and
    /// the addressing granularity. Used by the multi-light pipeline
    /// where each light gets its own colour derived from a screen zone.
    /// </summary>
    public Task SetLightColorAsync(string lightId, LightColor color, CancellationToken ct = default)
        => PutStateAsync(
            $"api/{_credentials!.Username}/lights/{lightId}/state",
            color,
            target: $"light_id={lightId}",
            ct);

    // Shared body for the two PUT-state endpoints. CLIP v1 accepts the
    // exact same payload shape on /groups/{id}/action and
    // /lights/{id}/state — `on`, `bri`, `xy`, `transitiontime` — so the
    // conversion + body building + log line live here and the public
    // entry points just pick the URL and a log-friendly target tag.
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
            // Black → turn the light off. Sending on:false alone is
            // enough ; xy and bri are ignored by the bridge when
            // on:false is present. transitiontime still applies and
            // gives a 100 ms fade-out instead of a hard cut.
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
    /// Asks the bridge to flash the addressed light so the user can
    /// spot it physically among other fixtures in the room. CLIP v1
    /// exposes the operation through the same /state endpoint with
    /// the <c>alert</c> field set to <c>lselect</c> (loop select :
    /// the bulb breathes / pulses for ~15 s and then auto-restores
    /// its previous state — we don't need to clean up). The bridge
    /// ACKs the PUT immediately, the visible flash continues in the
    /// background. Pair with <see cref="StopIdentifyAsync"/> when the
    /// caller wants to cut the flash short (typical : ~3 s in the
    /// Playground UI so the user isn't subjected to a 15 s strobe).
    /// </summary>
    public Task IdentifyLightAsync(string lightId, CancellationToken ct = default)
        => SendAlertAsync(lightId, "lselect", "start", ct);

    /// <summary>
    /// Stops an ongoing identify flash on the addressed light by
    /// setting <c>alert=none</c>. Idempotent — calling on a light
    /// that isn't currently flashing is harmless ; the bridge just
    /// acknowledges. The bridge restores the light's pre-flash state
    /// with a soft transition.
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
    /// Lists the lights that belong to the given group, in the bridge's
    /// own order. CLIP v1 doesn't return light metadata on the group
    /// endpoint — only the array of light ids — so we issue two GETs :
    /// one for the group (to get the id list) and one for <c>/lights</c>
    /// (to map each id to its human name + reachability flag). Two
    /// round-trips at "open settings" time is fine ; the multi-light
    /// push loop caches the result and doesn't re-query per tick.
    /// </summary>
    public async Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        DeckleLightingSource.Log.ListingLightsInGroup(groupId);

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
            DeckleLightingSource.Log.BridgeReturnedNoLights(groupId);
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
                // The group references a light id that isn't in the
                // /lights dictionary — shouldn't happen in practice
                // (the bridge maintains the invariant), but we keep
                // the entry with a synthetic name rather than dropping
                // it silently so the UI surfaces the discrepancy.
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

    // RFC1918 + APIPA validation for a Hue bridge address. The bridge
    // is a LAN-only device ; accepting an arbitrary IP would let any
    // untrusted source (manual entry, cloud discovery, a corrupted or
    // tampered settings.json) point a client at an attacker-controlled
    // server on the internet (SSRF / data exfil through the PUT-state
    // payload). The single home for this rule — the constructor calls
    // it before building the HttpClient, and the ambient push loop
    // re-checks the persisted IP through the same method for an early,
    // friendly message. V0 accepts only IPv4 in the canonical private
    // ranges and 169.254/16 link-local. IPv6 + hostnames are out of
    // scope for V0 ; revisit when a user requests it with a justified
    // setup.
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

    // Internal so HueEventStreamClient can reuse the exact same TLS
    // handling (self-signed cert callback) instead of duplicating the
    // setup. The SSE consumer needs its own HttpClient because it sets
    // Timeout to InfiniteTimeSpan (a streaming GET would otherwise be
    // cut off after 10 s), so we just hand it the factory.
    internal static HttpClient CreateBridgeHttpClient(string ip, int port, TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                // Bridge presents a self-signed cert ; we trust whoever
                // answers at the IP we explicitly chose. See class
                // header for the trade-off rationale.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };

        // Default port for the bridge is 443. The discovery endpoint
        // returns 443 explicitly ; older bridges that still expose
        // plain HTTP on 80 are out of scope for J2 (v2 firmware
        // forces HTTPS).
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{ip}:{port}/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };
    }

    private static string BuildDeviceType(string machineName)
    {
        // Sanitize : Hue rejects spaces and many punctuation chars in
        // the suffix. Keep alphanumeric + dash, fold the rest to dash,
        // cap at 33 chars to fit the 40-char total limit.
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
// "link button not pressed". Wraps the bridge's own error type code
// so callers can branch on known cases (e.g. type 7 = invalid value).
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
