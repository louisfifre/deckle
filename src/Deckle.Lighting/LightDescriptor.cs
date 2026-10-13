namespace Deckle.Lighting;

// Driver-neutral identity for a single light addressable through an
// IMultiLightOutput. The Id is whatever the driver uses to address the
// light (CLIP v1 integer-as-string for Hue); consumers treat it as an
// opaque string. Name is the label set in the vendor's app, never
// translated. IsReachable is a UI signal only: pushes still go out.
public sealed record LightDescriptor(string Id, string Name, bool IsReachable);
