namespace Deckle.Input;

// One mouse-wheel transition, normalized from either WM_INPUT (RIM_TYPEMOUSE)
// or the low-level mouse hook's WM_MOUSEWHEEL / WM_MOUSEHWHEEL stream: the
// signed detent delta (±120 per notch on a classic wheel, finer substeps or
// batched multiples on high-resolution wheels), the axis it rode, and the
// source device handle when Raw Input supplied one.
//
// TimestampMs uses the shared host clock so the wheel and contact streams
// line up directly when compared. Raw Input records receipt time; the hook
// translates MSLLHOOKSTRUCT.time into that clock so callback scheduling does
// not rewrite physical cadence. Device is the raw HID handle (hDevice) when
// Source is RawInput; 0 when the message hook supplied no device handle.
public readonly record struct MouseWheelEvent(
    WheelAxis Axis,
    short Delta,
    double TimestampMs,
    IntPtr Device,
    WheelEventSource Source,
    bool IsInjected = false,
    WheelInputState InputState = WheelInputState.None);

[Flags]
public enum WheelInputState
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    LeftButton = 1 << 3,
    RightButton = 1 << 4,
    MiddleButton = 1 << 5,
    XButton1 = 1 << 6,
    XButton2 = 1 << 7,
}

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

internal static class MouseWheelTimestamp
{
    // Both native values are unsigned millisecond counters. Subtraction in
    // uint space keeps the age correct across the roughly 49-day wrap.
    public static double ToSharedClock(
        uint eventTime,
        uint currentTick,
        double currentSharedMs)
    {
        uint ageMs = unchecked(currentTick - eventTime);
        return ageMs <= int.MaxValue
            ? currentSharedMs - ageMs
            : currentSharedMs;
    }
}
