using System;

namespace Deckle.Diagnostics.Logging;

// Whether the LogWindow follows the end of the log, the way a terminal or the
// Visual Studio Output window does: reading back up pauses it, coming back to
// the bottom resumes it. The window feeds every scroll observation here and
// shows IsFollowing on its Auto-scroll toggle; the logic stays out of XAML so
// it can be tested without a window.
//
// Only the reader pauses it. A scroll is the reader's when the offset moved up
// while the content kept its height: new rows grow the extent and leave the
// offset alone, and the 5000-row ring dropping its oldest rows shrinks the
// extent and moves the offset with it, so neither is mistaken for reading.
//
// A pause the reader chose with the toggle stays until they turn it back on:
// reaching the bottom resumes only a pause that scrolling caused.
public sealed class LogAutoScroll
{
    // Offsets are DIPs. The bottom is a band, not a point, because a wheel or
    // a layout rounding can stop a fraction short of ScrollableHeight.
    public const double BottomTolerance = 2.0;

    private double _lastOffset = double.NaN;
    private double _lastExtent = double.NaN;
    private bool _pausedByScrolling;

    public bool IsFollowing { get; private set; } = true;

    // The toggle, set by the reader: it overrides whatever scrolling decided.
    public void SetFollowing(bool following)
    {
        IsFollowing = following;
        _pausedByScrolling = false;
    }

    // One scroll observation. Returns true when IsFollowing changed, so the
    // window updates its toggle only then.
    public bool Observe(double offset, double scrollableHeight, double extentHeight)
    {
        bool contentChanged = !double.IsNaN(_lastExtent)
            && Math.Abs(extentHeight - _lastExtent) > BottomTolerance;
        bool movedUp = !double.IsNaN(_lastOffset)
            && offset < _lastOffset - BottomTolerance;
        _lastOffset = offset;
        _lastExtent = extentHeight;

        bool atBottom = scrollableHeight - offset <= BottomTolerance;
        if (atBottom)
        {
            if (IsFollowing || !_pausedByScrolling) return false;
            IsFollowing = true;
            _pausedByScrolling = false;
            return true;
        }

        if (IsFollowing && movedUp && !contentChanged)
        {
            IsFollowing = false;
            _pausedByScrolling = true;
            return true;
        }

        return false;
    }
}
