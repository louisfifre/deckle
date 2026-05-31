namespace Deckle.Diagnostics.Logging;

// User-facing settings of the live LogWindow. POCO with per-module
// persistence — loaded / saved by LoggingSettingsService.
//
// Minimal layout : une seule gate pour le moment, dédiée au filtrage
// runtime du bruit de la capture loop ambient. ApplicationLogToDisk
// vit côté TelemetrySettings (Diagnostics.Telemetry) parce que c'est
// une gate de persistance disque, pas d'émission. La SelectorBar
// filter state du LogWindow reste UI-local pour l'instant ; elle
// migrera ici quand le LogWindow XAML emménagera dans ce module.
public sealed class LoggingSettings
{
    // When false and an ambient capture loop is active, Verbose events
    // from the ambient providers are dropped by the live LogWindow
    // listener and the app.jsonl predicate. Reproduces the legacy
    // TelemetryService._captureActive filter without re-introducing a
    // central hub. Off-by-default — matches the user's quiet-by-
    // default preference for the noisy capture path.
    public bool LogAmbientCaptureActivity { get; set; } = false;
}
