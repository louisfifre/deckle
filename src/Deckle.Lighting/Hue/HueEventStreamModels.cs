using System.Text.Json.Serialization;

namespace Deckle.Lighting.Hue;

// Public record yielded by HueBridgeClient.StreamEventsAsync. One
// instance per resource that the bridge reports as changed. The
// caller (typically AmbientEngine) decides whether the change is
// truly external (a Hue app / Home Assistant / physical button
// press) or merely the bridge echoing back our own REST PUT, by
// comparing the event payload to its own last-pushed state for the
// matching v1 id.
//
// Fields are nullable because the EventStream sends partial updates
// — only the properties that actually changed are included. A
// resource that only changes brightness will have On = null and
// Xy = null.
public readonly record struct HueResourceUpdate(
    string V2ResourceId,
    string ResourceType,
    DateTimeOffset CreationTime,
    bool? On,
    int? Brightness,
    (float X, float Y)? Xy);

// Public maps from a one-shot CLIP v2 discovery call, used to
// translate event-side v2 UUIDs back to the v1 integer ids the
// REST push path uses. Lights and grouped_lights have disjoint
// UUID spaces, hence two dicts.
public sealed record HueV2IdMaps(
    IReadOnlyDictionary<string, string> Lights,
    IReadOnlyDictionary<string, string> GroupedLights);

// ── Internal wire DTOs ──────────────────────────────────────────────

internal sealed class HueV2ListResponse
{
    [JsonPropertyName("errors")] public object[]?         Errors { get; set; }
    [JsonPropertyName("data")]   public HueV2ResourceItem[]? Data { get; set; }
}

internal sealed class HueV2ResourceItem
{
    [JsonPropertyName("id")]    public string? Id    { get; set; }
    [JsonPropertyName("id_v1")] public string? IdV1  { get; set; }
    [JsonPropertyName("type")]  public string? Type  { get; set; }
}

// EventStream payload : an SSE `data:` line carries a JSON array of
// containers. Each container groups updates that fired in the same
// 1 s bridge window. Container.type is "update" for resource
// changes (other types exist : "add", "delete", "error").
internal sealed class HueEventStreamContainer
{
    [JsonPropertyName("type")]         public string?               Type         { get; set; }
    [JsonPropertyName("creationtime")] public DateTimeOffset        CreationTime { get; set; }
    [JsonPropertyName("data")]         public HueEventStreamData[]? Data         { get; set; }
}

internal sealed class HueEventStreamData
{
    [JsonPropertyName("id")]   public string? Id   { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("on")]   public HueEventStreamOnState?    On  { get; set; }
    [JsonPropertyName("dimming")] public HueEventStreamDimmingState? Bri { get; set; }
    [JsonPropertyName("color")] public HueEventStreamColorState? Xy { get; set; }
}

internal sealed class HueEventStreamOnState
{
    [JsonPropertyName("on")] public bool On { get; set; }
}

internal sealed class HueEventStreamDimmingState
{
    // CLIP v2 reports brightness as a 0..100 percentage, unlike the
    // CLIP v1 0..254 byte. Callers that need the v1 scale must
    // rescale themselves.
    [JsonPropertyName("brightness")] public int Bri { get; set; }
}

internal sealed class HueEventStreamColorState
{
    [JsonPropertyName("xy")] public HueEventStreamXy? Xy { get; set; }
}

internal sealed class HueEventStreamXy
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
}
