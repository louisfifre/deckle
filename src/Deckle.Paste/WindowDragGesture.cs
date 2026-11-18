namespace Deckle.Paste;

// One press on the Paste to folder window, and where the window goes while
// the pointer travels with the button held. A press that stays within the
// system's drag threshold is a click and moves nothing; once the pointer has
// left it, the window follows the pointer until the press ends, so the point
// that was pressed stays under the cursor.
//
// Positions are screen pixels. The window applies them itself
// (PasteWindow.Drag.cs): this class holds only the arithmetic, so a test can
// play a gesture without a window.
public sealed class WindowDragGesture
{
    private bool _pressed;
    private int _pressX;
    private int _pressY;
    private int _windowX;
    private int _windowY;

    // True once the pointer has left the threshold, until the press ends.
    public bool IsMoving { get; private set; }

    public void Press(int cursorX, int cursorY, int windowX, int windowY)
    {
        _pressed = true;
        IsMoving = false;
        _pressX = cursorX;
        _pressY = cursorY;
        _windowX = windowX;
        _windowY = windowY;
    }

    // Where the window's top-left corner goes for this pointer position, or
    // false while the press is still a click, or when nothing is pressed.
    public bool TryMove(int cursorX, int cursorY, int thresholdX, int thresholdY, out int windowX, out int windowY)
    {
        windowX = _windowX;
        windowY = _windowY;
        if (!_pressed) return false;

        int travelX = cursorX - _pressX;
        int travelY = cursorY - _pressY;
        if (!IsMoving && Math.Abs(travelX) < thresholdX && Math.Abs(travelY) < thresholdY) return false;

        IsMoving = true;
        windowX = _windowX + travelX;
        windowY = _windowY + travelY;
        return true;
    }

    public void End()
    {
        _pressed = false;
        IsMoving = false;
    }
}
