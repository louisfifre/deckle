namespace Deckle.Lighting.Ambient;

// Bridge that lets AmbientEngine read its settings without touching
// the App's SettingsService. The App-side implementation forwards each
// access to AmbientSettingsService.Instance.Current so live edits take
// effect on the next read without any event subscription.
//
// The engine reads the tuning, the zone assignments and the per-light
// brightness on every tick; the monitor, the multi-light flag, the
// capture rate and the Hue bridge fields only at start. It never writes
// back.
public interface IAmbientEngineHost
{
    AmbientSettings Ambient { get; }
}
