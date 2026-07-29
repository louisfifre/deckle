namespace Deckle.Input;

// The complete snapshot of touchpad contacts assembled from one Raw Input
// read — the unit the recognizer consumes (see CONTEXT.md § Input).
//
// ContactCount is the device's own declaration of how many contacts the
// frame carries, independent of their tip state; TipCount is the number
// of fingers actually on the surface. ScanTime is the device-side clock
// in 100 µs units (shared by every report of one frame — the hybrid-mode
// reassembly key); TimestampMs is the host-side receive time in
// milliseconds (fractional, from a monotonic clock), the basis for
// cadence and gap analysis. ReportCount says how many HID reports the
// frame spanned (1 = no fragmentation).
public sealed record ContactFrame(
    TouchpadContact[] Contacts,
    int ContactCount,
    bool ButtonDown,
    uint ScanTime,
    double TimestampMs,
    int ReportCount)
{
    /// <summary>Raw Input device that emitted this frame.</summary>
    public IntPtr DeviceHandle { get; init; }

    public int TipCount
    {
        get
        {
            int tips = 0;
            foreach (var c in Contacts)
                if (c.Tip) tips++;
            return tips;
        }
    }
}
