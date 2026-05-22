namespace Deckle.Diagnostics.Logging;

// User-facing settings of the live LogWindow. POCO with per-module
// persistence — loaded / saved by LoggingSettingsService.
//
// Wave 1 minimal layout: only the persistence gate and the capture-
// loop noise gate. The SelectorBar filter state remains UI-local for
// now (legacy LogWindow keeps its own selection). It will move here
// when the LogWindow XAML migrates to this module.
public sealed class LoggingSettings
{
    // Gate the JsonlEventListener that writes the general app.jsonl.
    // Off-by-default in preview releases, on in dev installs. Read on
    // every emission so toggling propagates immediately.
    public bool ApplicationLogToDisk { get; set; } = true;

    // When false and an ambient capture loop is active, ambient
    // providers (vision, lighting) drop their Verbose emissions before
    // hitting WriteEvent. Reproduces the legacy
    // TelemetryService._captureActive filter without re-introducing a
    // central hub. Off-by-default — matches the user's quiet-by-
    // default preference for the noisy capture path.
    public bool LogAmbientCaptureActivity { get; set; } = false;
}
