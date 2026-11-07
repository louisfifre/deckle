namespace Deckle.Lighting.Ambient;

// Bridge that lets AmbientEngine read its settings without touching
// the App's SettingsService. The App-side implementation forwards each
// access to AmbientSettingsService.Instance.Current so live edits take
// effect on the next read without any event subscription.
//
// The engine reads the tuning, the zone assignments and the per-light
// brightness on every tick; the monitor, the multi-light flag, the
// performance mode and the Hue bridge fields only at start. It never writes
// back.
public interface IAmbientEngineHost
{
    AmbientSettings Ambient { get; }

    // The shell's shared performance signal (Deckle.Settings
    // PerformanceSettings), which lowers the capture rate's ceiling. It is a
    // host member because the engine stays free of the shell's settings; a
    // host that does not wire it runs the normal tier.
    bool PerformanceMode => false;
}
