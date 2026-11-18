using Xunit;

namespace Deckle.Paste.Tests;

// The Paste to folder window stayed stuck to the cursor after a drag, because
// the move was handed to a loop that never saw the button's release
// (v0.135.0). The window now moves itself from this gesture: it follows the
// pointer while the button is held, and stops the moment the press ends.
[Trait("Category", "regression")]
public sealed class WindowDragGestureTests
{
    private const int Threshold = 4;

    [Fact]
    public void APressThatDoesNotTravelMovesNothing()
    {
        var gesture = new WindowDragGesture();
        gesture.Press(cursorX: 500, cursorY: 300, windowX: 400, windowY: 200);

        Assert.False(gesture.TryMove(502, 303, Threshold, Threshold, out _, out _));
        Assert.False(gesture.IsMoving);
    }

    [Fact]
    public void TheWindowFollowsThePointerOnceItHasLeftTheThreshold()
    {
        var gesture = new WindowDragGesture();
        gesture.Press(cursorX: 500, cursorY: 300, windowX: 400, windowY: 200);

        Assert.True(gesture.TryMove(530, 290, Threshold, Threshold, out int x, out int y));
        Assert.Equal((430, 190), (x, y));
    }

    [Fact]
    public void AMoveThatHasStartedGoesOnBackInsideTheThreshold()
    {
        var gesture = new WindowDragGesture();
        gesture.Press(cursorX: 500, cursorY: 300, windowX: 400, windowY: 200);
        gesture.TryMove(530, 290, Threshold, Threshold, out _, out _);

        Assert.True(gesture.TryMove(501, 300, Threshold, Threshold, out int x, out int y));
        Assert.Equal((401, 200), (x, y));
    }

    [Fact]
    public void TheWindowStopsFollowingWhenThePressEnds()
    {
        var gesture = new WindowDragGesture();
        gesture.Press(cursorX: 500, cursorY: 300, windowX: 400, windowY: 200);
        gesture.TryMove(530, 290, Threshold, Threshold, out _, out _);

        gesture.End();

        Assert.False(gesture.IsMoving);
        Assert.False(gesture.TryMove(700, 700, Threshold, Threshold, out _, out _));
    }

    [Fact]
    public void APointerThatMovesWithNoPressMovesNothing()
    {
        var gesture = new WindowDragGesture();

        Assert.False(gesture.TryMove(700, 700, Threshold, Threshold, out _, out _));
    }
}
