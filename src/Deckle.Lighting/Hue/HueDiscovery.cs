using System.Net.Http.Json;

namespace Deckle.Lighting;

// Bridge discovery on the local network. J2 only ships the cloud
// lookup path : a GET to discovery.meethue.com returns the IPs of the
// bridges that have phoned home from this WAN egress IP (typically
// everything on the user's local network). It's a Philips-hosted
// service but contains no auth-bearing data and works without an
// account — a thin convenience over the LAN-scan alternatives (mDNS
// `_hue._tcp.local.`, SSDP `IpBridge`).
//
// The mDNS path is the offline-friendly alternative ; it lands as a
// follow-up once the REST happy path is validated, since it requires
// either ~200 lines of DNS-over-UDP custom or a P/Invoke through
// `windns` / `DnsServiceBrowse`. For J2 first, cloud + manual IP fall-
// back covers the realistic cases (corporate firewall blocking the
// cloud lookup is rare for home users).
//
// The static HttpClient is intentionally process-wide. HttpClient is
// designed to be reused — instantiating one per call exhausts socket
// handles. The 10 s timeout matches the SLA Philips publishes for the
// discovery endpoint.
public static class HueDiscovery
{
    private const string CloudDiscoveryUrl = "https://discovery.meethue.com/";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Looks up Hue bridges reachable from the current WAN egress via
    /// the Philips-hosted discovery endpoint. Returns an empty list if
    /// no bridges are paired, or if the cloud service is unreachable
    /// — the latter case logs a Warning and surfaces nothing as an
    /// error so the caller can fall back to manual IP entry.
    /// </summary>
    public static async Task<IReadOnlyList<HueBridge>> DiscoverViaCloudAsync(CancellationToken ct = default)
    {
        DeckleLightingSource.Log.DiscoveryStarted();
        DeckleLightingSource.Log.DiscoveryStartedDetail(CloudDiscoveryUrl);

        try
        {
            var bridges = await _http.GetFromJsonAsync<HueBridge[]>(CloudDiscoveryUrl, ct)
                          ?? [];

            DeckleLightingSource.Log.DiscoveryFound();
            DeckleLightingSource.Log.DiscoveryFoundDetail(bridges.Length);
            foreach (var b in bridges)
            {
                DeckleLightingSource.Log.DiscoveryBridgeFound(b.Id, b.InternalIpAddress);
            }
            return bridges;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Cloud lookup is a convenience, not a requirement — log
            // Warning and return empty so the UI can prompt for manual
            // IP entry. TaskCanceledException covers both the explicit
            // CancellationToken path and the HttpClient.Timeout firing.
            DeckleLightingSource.Log.DiscoveryFailed();
            DeckleLightingSource.Log.DiscoveryFailedDetail(ex.GetType().Name, ex.Message);
            return [];
        }
    }
}
