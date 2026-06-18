namespace Deckle.Input;

// One mouse-wheel transition, normalized from either WM_INPUT (RIM_TYPEMOUSE)
// or the low-level mouse hook's WM_MOUSEWHEEL / WM_MOUSEHWHEEL stream: the
// signed detent delta (±120 per notch on a classic wheel, finer substeps or
// batched multiples on high-resolution wheels), the axis it rode, and the
// source device handle when Raw Input supplied one.
//
// TimestampMs is the shared host clock (RawInputHost.NowMs) — the same
// clock every input event in the module is stamped with, so the wheel and
// contact streams line up directly when compared. Device is the raw HID
// handle (hDevice) when Source is RawInput; 0 when the message hook is the
// source and Windows did not provide a device handle.
public readonly record struct MouseWheelEvent(
    WheelAxis Axis,
    short Delta,
    double TimestampMs,
    IntPtr Device,
    WheelEventSource Source);

// Which wheel a MouseWheelEvent rode: Vertical is the common scroll wheel
// (RI_MOUSE_WHEEL), Horizontal the tilt/side wheel (RI_MOUSE_HWHEEL).
public enum WheelAxis
{
    Vertical,
    Horizontal,
}

public enum WheelEventSource
{
    RawInput,
    MessageHook,
}
