using static Deckle.Orchestration.DisplayConfigNativeMethods;

namespace Deckle.Orchestration;

// One active monitor as the HDR rule sees it. GdiDeviceName ("\\.\DISPLAY1")
// is the key rules are stored under, the same key Ambient stores its monitor
// with. AdapterId and TargetId address the monitor in the CCD calls and are
// only valid until the display configuration changes.
internal sealed record DisplayTarget(
    string GdiDeviceName,
    string FriendlyName,
    bool HdrSupported,
    bool HdrEnabled,
    LUID AdapterId,
    uint TargetId);
