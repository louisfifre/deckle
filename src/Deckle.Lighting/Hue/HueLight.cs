namespace Deckle.Lighting;

// Hue "light" — a single addressable bulb / strip / fixture inside a
// group. CLIP v1 exposes lights by integer-string id (e.g. "1", "5") ;
// the name is set by the user in the Hue mobile app. Type carries the
// device class ("Extended color light", "Color temperature light", …).
//
// Unreachable lights still accept PUT /state calls (the bridge queues
// the latest state), so pushes are not skipped; the flag only tells the
// user why the lamp isn't responding.
public sealed record HueLight(string Id, string Name, string Type, bool Reachable)
{
    public string DisplayLabel => Reachable ? Name : $"{Name} (offline)";
}
