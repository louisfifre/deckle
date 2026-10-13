namespace Deckle.Catalog;

// Holds a slider's value while a pointer gesture is in progress, so the model is
// written once, at the release, and not on every detent the thumb crosses. The
// doctrine's rule: a change applies at once, written when its gesture ends. A
// keyboard or wheel step has no press and release of its own, so Offer lets it
// through straight away. Pure state, kept apart from the WinUI control so it can
// be tested without one.
public sealed class GestureHold
{
    private bool _holding;
    private double? _pending;

    // The pointer went down: from here on, offered values are held.
    public void Begin()
    {
        _holding = true;
        _pending = null;
    }

    // A new value from the control. Returns true when the caller should write it
    // now (no gesture in progress), false when it is held for the release.
    public bool Offer(double value)
    {
        if (!_holding) return true;
        _pending = value;
        return false;
    }

    // The pointer went up, or its capture was lost: returns the last held value
    // to write, or null when the gesture changed nothing. Ending twice is safe,
    // the second call finds nothing held.
    public double? End()
    {
        _holding = false;
        double? pending = _pending;
        _pending = null;
        return pending;
    }
}
