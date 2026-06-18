namespace Deckle.Autocorrect;

// What the gate knows about the control under keyboard focus.
// IsPassword is the hard gate — no decoding, no buffering, no counting.
// A surface that is not text-editable withholds corrections without
// stopping observation resets. Unknown (UIA could not answer) observes
// but never corrects — conservative on action, permissive on tracking.
// Probe is the raw UIA editability signature behind IsTextEditable
// (ControlType + pattern availability, or the failure reason when UIA could
// not answer) — diagnostic only, surfaced on the SurfaceChanged log line.
public sealed record FocusedSurface(string ProcessName, bool IsPassword, bool IsTextEditable, string Probe = "")
{
    public static readonly FocusedSurface Unknown = new(string.Empty, IsPassword: false, IsTextEditable: false);
}
