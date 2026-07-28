namespace Deckle.Input;

/// <summary>
/// One touchpad collection attached through Raw Input. The handle identifies
/// the device that emitted a frame for the lifetime of the current host.
/// </summary>
public sealed record TouchpadDevice(
    IntPtr Handle,
    TouchpadCapabilities Capabilities);
