using System.Net.Http.Json;

namespace Deckle.Lighting;

public sealed partial class HueBridgeClient
{
    /// <summary>
    /// Fetches the v2 ↔ v1 identifier maps for lights and grouped_lights.
    /// The CLIP v2 EventStream emits resource UUIDs (v2) whereas the
    /// REST CLIP v1 push path the engine uses takes integer ids (v1).
    /// </summary>
    public async Task<HueV2IdMaps> FetchV2IdMapsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        DeckleLightingSource.Log.FetchingV2IdMaps();

        var lights = await FetchV2ResourceMapAsync("light", ct).ConfigureAwait(false);
        var groups = await FetchV2ResourceMapAsync("grouped_light", ct).ConfigureAwait(false);

        DeckleLightingSource.Log.V2IdMapsFetched(lights.Count, groups.Count);
        return new HueV2IdMaps(lights, groups);
    }

    private async Task<Dictionary<string, string>> FetchV2ResourceMapAsync(
        string resourceType, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"clip/v2/resource/{resourceType}");
        request.Headers.Add("hue-application-key", _credentials!.Username);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<HueV2ListResponse>(_jsonOptions, ct).ConfigureAwait(false);
        var map = new Dictionary<string, string>(payload?.Data?.Length ?? 0, StringComparer.Ordinal);
        if (payload?.Data is null) return map;

        foreach (var entry in payload.Data)
        {
            if (string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.IdV1)) continue;
            int slash = entry.IdV1.LastIndexOf('/');
            var v1 = slash >= 0 ? entry.IdV1[(slash + 1)..] : entry.IdV1;
            if (v1.Length > 0) map[entry.Id] = v1;
        }
        return map;
    }

    public async Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        DeckleLightingSource.Log.ListingEntertainmentConfigs();

        var entResponse = await GetV2Async<HueV2Response<HueV2EntertainmentConfigDto>>(
            "resource/entertainment_configuration", ct).ConfigureAwait(false);

        if (entResponse?.Data is null || entResponse.Data.Count == 0)
        {
            DeckleLightingSource.Log.EntertainmentEmpty();
            return [];
        }

        var v1Names = await FetchV2LightNamesAsync(ct).ConfigureAwait(false);
        var entUuidToV1 = await FetchV2EntertainmentLightMapAsync(ct).ConfigureAwait(false);
        DeckleLightingSource.Log.EntertainmentV2Catalog(entUuidToV1.Count, v1Names.Count);

        var areas = new List<HueEntertainmentArea>(entResponse.Data.Count);
        foreach (var ent in entResponse.Data)
        {
            var placements = new List<HueLightPlacement>();
            var channels = new List<HueEntertainmentChannel>();
            if (ent.Locations?.ServiceLocations is not null)
            {
                foreach (var loc in ent.Locations.ServiceLocations)
                {
                    if (loc.Service?.Rid is null) continue;
                    if (!entUuidToV1.TryGetValue(loc.Service.Rid, out var v1Id)) continue;

                    if (!TryGetPlacementCentroid(loc, out double x, out double y, out double z))
                        continue;

                    var name = v1Names.TryGetValue(v1Id, out var nm) ? nm : $"Light {v1Id}";
                    placements.Add(new HueLightPlacement(v1Id, name, x, y, z));
                }
            }

            if (ent.Channels is not null)
            {
                foreach (var channel in ent.Channels)
                {
                    var serviceIds = ExtractChannelServiceIds(channel);
                    string? v1Id = null;
                    string name = "";
                    foreach (var serviceId in serviceIds)
                    {
                        if (!entUuidToV1.TryGetValue(serviceId, out v1Id)) continue;
                        name = v1Names.TryGetValue(v1Id, out var nm) ? nm : $"Light {v1Id}";
                        break;
                    }

                    var p = channel.Position;
                    channels.Add(new HueEntertainmentChannel(
                        channel.ChannelId,
                        v1Id,
                        name,
                        p?.X ?? 0,
                        p?.Y ?? 0,
                        p?.Z ?? 0,
                        serviceIds));
                }
            }

            areas.Add(new HueEntertainmentArea(
                ent.Id ?? "",
                ent.Metadata?.Name ?? "",
                placements,
                channels));
        }

        DeckleLightingSource.Log.EntertainmentListed(areas.Count);
        foreach (var area in areas)
        {
            DeckleLightingSource.Log.EntertainmentArea(area.Id, area.Name, area.LightPlacements.Count);
            foreach (var p in area.LightPlacements)
            {
                DeckleLightingSource.Log.PlacementListed(area.Id, p.LightId, p.Name, p.X, p.Y, p.Z);
            }
        }
        return areas;
    }

    public async Task SetEntertainmentStreamingAsync(
        string entertainmentConfigurationId,
        bool active,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsurePaired();

        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"clip/v2/resource/entertainment_configuration/{entertainmentConfigurationId}");
        request.Headers.Add("hue-application-key", _credentials!.Username);
        request.Content = JsonContent.Create(
            new HueV2EntertainmentActionRequest { Action = active ? "start" : "stop" },
            options: _jsonOptions);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            DeckleLightingSource.Log.ClipV2GetFailed();
            DeckleLightingSource.Log.ClipV2GetFailedDetail("resource/entertainment_configuration/{id}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        var payload = await response.Content
            .ReadFromJsonAsync<HueV2Response<object>>(_jsonOptions, ct)
            .ConfigureAwait(false);
        ThrowIfV2Errors(payload, "resource/entertainment_configuration/{id}");
    }

    private static List<string> ExtractChannelServiceIds(HueV2EntertainmentChannelDto channel)
    {
        var ids = new List<string>();
        if (channel.Members is null) return ids;

        foreach (var member in channel.Members)
        {
            if (!string.IsNullOrEmpty(member.Service?.Rid))
                ids.Add(member.Service.Rid);
        }
        return ids;
    }

    private async Task<Dictionary<string, string>> FetchV2LightNamesAsync(CancellationToken ct)
    {
        var v1Names = new Dictionary<string, string>();
        var lightsResponse = await GetV2Async<HueV2Response<HueV2LightDto>>(
            "resource/light", ct).ConfigureAwait(false);
        if (lightsResponse?.Data is null) return v1Names;

        foreach (var lt in lightsResponse.Data)
        {
            if (string.IsNullOrEmpty(lt.IdV1)) continue;
            int slash = lt.IdV1.LastIndexOf('/');
            if (slash < 0 || slash >= lt.IdV1.Length - 1) continue;
            var v1Id = lt.IdV1[(slash + 1)..];
            if (!string.IsNullOrEmpty(lt.Metadata?.Name))
                v1Names[v1Id] = lt.Metadata.Name;
        }
        return v1Names;
    }

    private async Task<Dictionary<string, string>> FetchV2EntertainmentLightMapAsync(CancellationToken ct)
    {
        var entUuidToV1 = new Dictionary<string, string>();
        var entServiceResponse = await GetV2Async<HueV2Response<HueV2EntertainmentServiceDto>>(
            "resource/entertainment", ct).ConfigureAwait(false);
        if (entServiceResponse?.Data is null) return entUuidToV1;

        foreach (var es in entServiceResponse.Data)
        {
            if (string.IsNullOrEmpty(es.Id) || string.IsNullOrEmpty(es.IdV1)) continue;
            int slash = es.IdV1.LastIndexOf('/');
            if (slash < 0 || slash >= es.IdV1.Length - 1) continue;
            entUuidToV1[es.Id] = es.IdV1[(slash + 1)..];
        }
        return entUuidToV1;
    }

    private static bool TryGetPlacementCentroid(
        HueV2ServiceLocation loc,
        out double x,
        out double y,
        out double z)
    {
        double sumX = 0, sumY = 0, sumZ = 0;
        int n = 0;
        if (loc.Positions is not null)
        {
            foreach (var p in loc.Positions)
            {
                sumX += p.X;
                sumY += p.Y;
                sumZ += p.Z;
                n++;
            }
        }
        if (n == 0 && loc.Position is not null)
        {
            sumX = loc.Position.X;
            sumY = loc.Position.Y;
            sumZ = loc.Position.Z;
            n = 1;
        }

        if (n == 0)
        {
            x = y = z = 0;
            return false;
        }

        x = sumX / n;
        y = sumY / n;
        z = sumZ / n;
        return true;
    }

    private async Task<T?> GetV2Async<T>(string path, CancellationToken ct) where T : class
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"clip/v2/{path}");
        request.Headers.Add("hue-application-key", _credentials!.Username);
        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            DeckleLightingSource.Log.ClipV2GetFailed();
            DeckleLightingSource.Log.ClipV2GetFailedDetail(path, (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
        var payload = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct).ConfigureAwait(false);
        if (payload is IHueV2Response objectResponse)
            ThrowIfV2Errors(objectResponse, path);
        return payload;
    }

    private static void ThrowIfV2Errors(IHueV2Response? payload, string path)
    {
        if (payload?.Errors is null || payload.Errors.Count == 0) return;

        string message = payload.Errors[0].Description ?? "Unknown CLIP v2 error.";
        DeckleLightingSource.Log.ClipV2GetFailed();
        DeckleLightingSource.Log.ClipV2GetFailedDetail(path, 200);
        throw new HttpRequestException(
            $"Hue CLIP v2 request failed: {message}",
            inner: null,
            System.Net.HttpStatusCode.OK);
    }
}
