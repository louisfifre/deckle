namespace Deckle.Lighting;

// Hue "group" — the unit of control for the REST API. Maps to a
// Room (e.g. "Salon"), a Zone, or an Entertainment area in the user's
// Hue app. CLIP v1 doesn't distinguish at the API surface — they're
// all addressable by group_id and accept the same `/action` payload.
// Type is the bridge's own classification ("Room", "Zone",
// "Entertainment", "LightGroup", …), passed through verbatim.
public sealed record HueGroup(string Id, string Name, string Type, int LightsCount)
{
    public string DisplayLabel => LightsCount switch
    {
        0 => $"{Name} ({Type}, no lights)",
        1 => $"{Name} ({Type}, 1 light)",
        _ => $"{Name} ({Type}, {LightsCount} lights)",
    };
}
