namespace Deckle.Lighting;

// One entertainment area as configured by the user in the Hue mobile
// app : a named bundle of lights with explicit 3D positions relative
// to the user's TV / room, kept by the bridge across reboots.
//
// Two consumers: the Entertainment v2 streaming driver, whose channels
// identify what a HueStream datagram addresses, and Ambient, whose zone
// suggestions start from the placements so the user does not re-tag each
// light by hand.
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
// are collapsed to their centroid: Ambient doesn't model strip topology.
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
