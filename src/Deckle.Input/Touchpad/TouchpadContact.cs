namespace Deckle.Input;

// One finger inside a contact frame, as reported by the device: a stable
// identifier for the finger across frames, its position in the touchpad's
// logical coordinate space, and two per-contact bits — the tip switch
// (finger actually on the surface) and the confidence bit (the device
// vouches this is a finger, not a palm).
//
// A contact can legitimately appear with Tip=false during the lift
// transition: the Precision Touchpad spec requires a lifting contact to
// be reported once more, tip clear, at its last on-surface position.
// Consumers that count fingers must therefore count tips, not array
// entries — that distinction is what lets the recognizer read finger
// lifts from the frames instead of inferring them from inter-frame
// silence.
public readonly record struct TouchpadContact(int Id, int X, int Y, bool Tip, bool Confidence);
