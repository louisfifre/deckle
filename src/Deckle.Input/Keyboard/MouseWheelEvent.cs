namespace Deckle.Input;

// One mouse-wheel transition from WM_INPUT (RIM_TYPEMOUSE) whose button
// flags carried RI_MOUSE_WHEEL or RI_MOUSE_HWHEEL, normalized: the signed
// detent delta straight from RAWMOUSE.usButtonData (±120 per notch on a
// classic wheel, finer substeps or batched multiples on high-resolution
// wheels), the axis it rode, and the source device handle so a consumer
// can tell devices apart and measure per-device cadence.
//
// TimestampMs is the shared host clock (RawInputHost.NowMs) — the same
// clock every input event in the module is stamped with, so the wheel and
// contact streams line up directly when compared. Device is the raw HID
// handle (hDevice); 0 marks a synthetic event, mirroring the IsInjected
// convention of KeyboardKeyEvent.
public readonly record struct MouseWheelEvent(
    WheelAxis Axis,
    short Delta,
    double TimestampMs,
    IntPtr Device);

// Which wheel a MouseWheelEvent rode: Vertical is the common scroll wheel
// (RI_MOUSE_WHEEL), Horizontal the tilt/side wheel (RI_MOUSE_HWHEEL).
public enum WheelAxis
{
    Vertical,
    Horizontal,
}
