namespace Deckle.Vision;

// Closed vocabulary of why a capture session ended. The same value reaches
// the reason field of ScreenCaptureStopped and CaptureLoopEnded and the
// argument of ScreenCaptureService.Stopped, so a consumer that relays it in
// its own milestone spells it the way the Vision lines already do.
public static class CaptureStopReasons
{
    // Stop() cancelled the loop; Stopped is not raised for it.
    public const string Requested = "requested";

    // DXGI reported DEVICE_REMOVED or DEVICE_HUNG.
    public const string DeviceLost = "device_lost";

    // AcquireNextFrame kept returning INVALID_CALL past the recovery bound.
    public const string FrameOwnershipAbandoned = "frame_ownership_abandoned";

    // The duplication could not be reopened after an unexpected failure.
    public const string RecreateAbandoned = "recreate_abandoned";

    // An exception escaped the capture loop.
    public const string LoopCrashed = "loop_crashed";
}
