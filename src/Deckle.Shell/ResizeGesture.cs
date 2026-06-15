namespace Deckle.Shell;

// Pure, Win32-free state machine behind ResizeCoalescer. Folded out so the one
// decision it makes — when a resize has "settled" and a recompute is due — is
// unit testable without a real HWND or message pump (same split as HudWindow's
// ProximityRollupAggregator). ResizeCoalescer feeds it the raw window messages
// and turns a non-null settlement into the recompute callback plus the trace.
//
// The model in one paragraph: a user drag is bracketed by WM_ENTERSIZEMOVE …
// WM_EXITSIZEMOVE, with a burst of WM_SIZE in between. While that gesture is in
// flight IsResizing is true and every WM_SIZE is swallowed — that is the
// per-frame recompute the window would otherwise pay — counted only so the
// trace can report how many frames were coalesced. The single settlement is
// emitted on EXIT. A WM_SIZE arriving with no gesture in flight (maximize, snap,
// programmatic SetWindowPos — none of which enter the modal loop) is the safety
// net: it settles immediately, since no EXIT will ever come to close it.
internal sealed class ResizeGesture
{
    public bool IsResizing { get; private set; }

    private bool _sizedDuringGesture;
    private int  _frames;

    // Gesture opened (WM_ENTERSIZEMOVE). Never settles on its own — even a bare
    // title-bar grab with no drag still pairs with the EXIT below.
    public void EnterSizeMove()
    {
        IsResizing = true;
        _sizedDuringGesture = false;
        _frames = 0;
    }

    // Gesture closed (WM_EXITSIZEMOVE). Settles once, as a "gesture", but only if
    // the drag actually changed the size: a move-only gesture (dragging the title
    // bar without resizing) must not trigger a layout/redraw recompute. An EXIT
    // with no matching ENTER is ignored — defensive, the loop should be balanced.
    public ResizeSettlement? ExitSizeMove()
    {
        if (!IsResizing) return null;
        IsResizing = false;
        if (!_sizedDuringGesture) return null;
        return new ResizeSettlement(ResizeTrigger.Gesture, _frames);
    }

    // A WM_SIZE landed (the caller has already filtered out the minimize case).
    // Inside a gesture: swallow and count — the recompute is deferred to EXIT.
    // Outside a gesture: settle now as "direct" (the safety net for maximize,
    // snap and programmatic resizes that never enter the modal loop).
    public ResizeSettlement? Size()
    {
        if (IsResizing)
        {
            _sizedDuringGesture = true;
            _frames++;
            return null;
        }
        return new ResizeSettlement(ResizeTrigger.Direct, Frames: 1);
    }
}

// What a settled resize carries to the trace. `Frames` is how many WM_SIZE were
// coalesced — ≥ 1 for a gesture, exactly 1 for a direct settlement.
internal readonly record struct ResizeSettlement(ResizeTrigger Trigger, int Frames);

// Closed vocabulary for the resize trace's `trigger` field.
internal enum ResizeTrigger
{
    Gesture, // user drag, bracketed by WM_ENTERSIZEMOVE … WM_EXITSIZEMOVE
    Direct,  // WM_SIZE with no gesture: maximize / snap / programmatic resize
}
