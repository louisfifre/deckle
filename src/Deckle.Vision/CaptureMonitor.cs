namespace Deckle.Vision;

/// <summary>
/// Public, handle-free snapshot of a display that can be used as a screen
/// capture source. <see cref="DeviceName"/> is the opaque stable value to persist.
/// </summary>
public sealed record CaptureMonitor(
    string DeviceName,
    bool IsPrimary,
    int Width,
    int Height);
