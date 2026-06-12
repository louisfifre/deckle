namespace Deckle.Input;

// Static description of one Precision Touchpad device, discovered once
// from its preparsed HID data and device info — written at the head of
// every telemetry session so a recorded file is self-describing, and
// used by consumers to express thresholds relative to the device's
// logical coordinate space instead of absolute magic numbers.
//
// ContactSlots is the number of per-contact link collections in the
// report layout — the ceiling on contacts per HID report, not the number
// of fingers currently down.
public sealed record TouchpadCapabilities(
    string DeviceName,
    uint VendorId,
    uint ProductId,
    int XMin,
    int XMax,
    int YMin,
    int YMax,
    int ContactSlots,
    int ReportByteLength)
{
    public int XRange => XMax - XMin;
    public int YRange => YMax - YMin;
}
