using System.Net.Http.Json;

namespace Deckle.Lighting;

// Public discovery facade. Local DNS-SD is the default because a Hue bridge is
// a LAN device and Philips deprecated its older UPnP/SSDP discovery path. The
// hosted endpoint remains available only as an explicitly requested fallback.
public static class HueDiscovery
{
    private const string CloudDiscoveryUrl = "https://discovery.meethue.com/";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Finds Hue bridges advertised on the local link as
    /// <c>_hue._tcp.local</c>. No request leaves the local network.
    /// </summary>
    public static Task<IReadOnlyList<HueBridge>> DiscoverLocalAsync(CancellationToken ct = default)
        => HueLocalDiscovery.DiscoverAsync(ct);

    /// <summary>
    /// Looks up Hue bridges associated with the current public IP through the
    /// Philips-hosted endpoint. This is an explicit fallback, never the default.
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
            foreach (var bridge in bridges)
            {
                DeckleLightingSource.Log.DiscoveryBridgeFound(
                    bridge.Id,
                    bridge.InternalIpAddress);
            }
            return bridges;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            DeckleLightingSource.Log.DiscoveryFailed();
            DeckleLightingSource.Log.DiscoveryFailedDetail(ex.GetType().Name, ex.Message);
            return [];
        }
    }
}
