using System.Text.Json.Serialization;

namespace Deckle.Lighting;

public sealed partial class HueBridgeClient
{
    private sealed class HueConfigDto
    {
        [JsonPropertyName("bridgeid")] public string? BridgeId { get; set; }
    }

    private sealed class HuePairRequest
    {
        [JsonPropertyName("devicetype")]        public string DeviceType { get; set; } = "";
        [JsonPropertyName("generateclientkey")] public bool   GenerateClientKey { get; set; }
    }

    private sealed class HueApiResponseElement
    {
        [JsonPropertyName("success")] public HueSuccessPayload? Success { get; set; }
        [JsonPropertyName("error")]   public HueErrorPayload?   Error   { get; set; }
    }

    private sealed class HueSuccessPayload
    {
        [JsonPropertyName("username")]  public string Username  { get; set; } = "";
        [JsonPropertyName("clientkey")] public string ClientKey { get; set; } = "";
    }

    private sealed class HueErrorPayload
    {
        [JsonPropertyName("type")]        public int    Type        { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; } = "";
    }

    private sealed class HueGroupDto
    {
        [JsonPropertyName("name")]   public string?   Name   { get; set; }
        [JsonPropertyName("lights")] public string[]? Lights { get; set; }
        [JsonPropertyName("type")]   public string?   Type   { get; set; }
    }

    // Body schema for PUT /groups/{id}/action and PUT /lights/{id}/state.
    // CLIP v1 accepts the exact same field set on both endpoints, so we
    // share a single DTO.
    private sealed class HueStateRequest
    {
        // Nullable on purpose: only the fields we set get serialised,
        // thanks to JsonIgnoreCondition.WhenWritingNull in _jsonOptions.
        // For black we send {"on":false,"transitiontime":1}, for a
        // colour {"on":true,"bri":...,"xy":[...],"transitiontime":1}.
        [JsonPropertyName("on")]             public bool?     On             { get; set; }
        [JsonPropertyName("bri")]            public byte?     Brightness     { get; set; }
        [JsonPropertyName("xy")]             public double[]? Xy             { get; set; }
        [JsonPropertyName("transitiontime")] public int?      TransitionTime { get; set; }
    }

    private sealed class HueLightDto
    {
        [JsonPropertyName("name")]  public string?           Name  { get; set; }
        [JsonPropertyName("type")]  public string?           Type  { get; set; }
        [JsonPropertyName("state")] public HueLightStateDto? State { get; set; }
    }

    private sealed class HueLightStateDto
    {
        // The bridge returns many fields here (on, bri, xy, ct, alert, ...).
        // We only project the reachability flag for now; the colour
        // pipeline pushes state, it doesn't read it back.
        [JsonPropertyName("reachable")] public bool? Reachable { get; set; }
    }

    private sealed class HueAlertRequest
    {
        // Hue CLIP v1 alert values: "none" (clear), "select" (one
        // breathe cycle), "lselect" (loop for ~15 s then auto-revert).
        // We only ever send "lselect" for the Identify pattern.
        [JsonPropertyName("alert")] public string Alert { get; set; } = "lselect";
    }

    // CLIP v2 wraps every collection response in {"data":[...], "errors":[...]}.
    // We only project the fields we consume here.
    private interface IHueV2Response
    {
        List<HueV2ErrorDto>? Errors { get; }
    }

    private sealed class HueV2Response<T> : IHueV2Response
    {
        [JsonPropertyName("data")] public List<T>? Data { get; set; }
        [JsonPropertyName("errors")] public List<HueV2ErrorDto>? Errors { get; set; }
    }

    private sealed class HueV2ErrorDto
    {
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class HueV2LightDto
    {
        [JsonPropertyName("id")]       public string?        Id       { get; set; }
        [JsonPropertyName("id_v1")]    public string?        IdV1     { get; set; }
        [JsonPropertyName("metadata")] public HueV2Metadata? Metadata { get; set; }
    }

    // /clip/v2/resource/entertainment exposes the streaming endpoint
    // attached to each colour-capable Hue light.
    private sealed class HueV2EntertainmentServiceDto
    {
        [JsonPropertyName("id")]    public string? Id   { get; set; }
        [JsonPropertyName("id_v1")] public string? IdV1 { get; set; }
    }

    private sealed class HueV2EntertainmentConfigDto
    {
        [JsonPropertyName("id")]        public string?         Id        { get; set; }
        [JsonPropertyName("metadata")]  public HueV2Metadata?  Metadata  { get; set; }
        [JsonPropertyName("locations")] public HueV2Locations? Locations { get; set; }
        [JsonPropertyName("channels")]  public List<HueV2EntertainmentChannelDto>? Channels { get; set; }
    }

    private sealed class HueV2Metadata
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class HueV2Locations
    {
        [JsonPropertyName("service_locations")] public List<HueV2ServiceLocation>? ServiceLocations { get; set; }
    }

    private sealed class HueV2ServiceLocation
    {
        [JsonPropertyName("service")]   public HueV2ResourceRef?    Service   { get; set; }
        [JsonPropertyName("position")]  public HueV2Position?       Position  { get; set; }
        [JsonPropertyName("positions")] public List<HueV2Position>? Positions { get; set; }
    }

    private sealed class HueV2EntertainmentChannelDto
    {
        [JsonPropertyName("channel_id")] public int ChannelId { get; set; }
        [JsonPropertyName("position")]   public HueV2Position? Position { get; set; }
        [JsonPropertyName("members")]    public List<HueV2EntertainmentMemberDto>? Members { get; set; }
    }

    private sealed class HueV2EntertainmentMemberDto
    {
        [JsonPropertyName("service")] public HueV2ResourceRef? Service { get; set; }
    }

    private sealed class HueV2EntertainmentActionRequest
    {
        [JsonPropertyName("action")] public string Action { get; set; } = "";
    }

    private sealed class HueV2ResourceRef
    {
        [JsonPropertyName("rid")]   public string? Rid  { get; set; }
        [JsonPropertyName("rtype")] public string? Type { get; set; }
    }

    private sealed class HueV2Position
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
    }

    private abstract record PairOutcome
    {
        public sealed record Success(HueCredentials Credentials) : PairOutcome;
        public sealed record LinkButtonNotPressed : PairOutcome;
        public sealed record OtherError(int Type, string Description) : PairOutcome;
    }
}
