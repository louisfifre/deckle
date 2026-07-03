namespace Deckle.Lighting;

// One entertainment area as configured by the user in the Hue mobile
// app : a named bundle of lights with explicit 3D positions relative
// to the user's TV / room. Hue keeps these positions in the bridge
// state across reboots ; the user only ever has to set them once
// (drag-and-drop in the Hue app's "Entertainment area" setup flow).
//
// We surface entertainment areas here for one reason only :
// pre-filling the Ambient module's per-light zone assignment. If a
// user already placed their lights in an entertainment area, we
// shouldn't ask them to re-tag each one "Top / Bottom / Left / Right"
// — we derive the suggestion from the position and let them override
// if our heuristic gets it wrong.
//
// The resting CLIP v2 configuration is also the catalogue the
// Entertainment v2 streaming driver uses: channels identify what a
// HueStream datagram addresses, placements help Ambient suggest zones.
public sealed record HueEntertainmentArea(
    string Id,
    string Name,
    IReadOnlyList<HueLightPlacement> LightPlacements,
    IReadOnlyList<HueEntertainmentChannel> Channels);

// Normalised 3D position of one light inside its entertainment area,
// keyed by the CLIP v1 integer-as-string light id so consumers can
// match against what <see cref="HueBridgeClient.ListLightsInGroupAsync"/>
// returns. The light's user-facing name is carried alongside so
// consumers that fall back to the entertainment area (because the
// underlying v1 group reports no lights — common for pure Entertainment
// areas) can still build a sensible UI without a second REST call.
//
// Coordinate convention (Hue Entertainment, both v1 and v2) :
//   X : -1 = left of TV, +1 = right.
//   Y : -1 = behind viewer (back of room), +1 = behind TV (front wall).
//   Z : -1 = floor, +1 = ceiling.
//
// Multi-position fixtures (LED strips with `positions[]` of length > 1)
// are collapsed to their centroid before reaching this record — V0
// Ambient doesn't model strip topology.
public sealed record HueLightPlacement(
    string LightId,
    string Name,
    double X,
    double Y,
    double Z);

// One HueStream-addressable channel inside an entertainment area.
// ChannelId is the byte-sized number the bridge expects in each
// HueStream frame; ServiceIds are CLIP v2 entertainment-service UUIDs
// attached to that channel. Deckle keeps the v1 LightId when it can
// translate the service UUID because Ambient settings and placement UI
// already key per-light state by the driver-facing id.
public sealed record HueEntertainmentChannel(
    int ChannelId,
    string? LightId,
    string Name,
    double X,
    double Y,
    double Z,
    IReadOnlyList<string> ServiceIds);
