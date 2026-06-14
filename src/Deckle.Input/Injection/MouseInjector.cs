using System.Runtime.InteropServices;
using Deckle.Input;

namespace Deckle.Input;

// Synthesizes primary-button state and relative pointer motion through
// SendInput — the injection primitive consumers build gestures on.
//
// Moves are RELATIVE (no MOUSEEVENTF_ABSOLUTE): the deltas ride the
// system pointer path like a physical mouse, including the Windows
// pointer ballistics the user already calibrated. SendInput takes
// integer mickeys, so the fractional remainder of every move
// accumulates and is re-injected once it crosses a whole unit —
// without this, slow precise drags lose most of their sub-pixel
// motion (same accumulation the ThreeFingerDragOnWindows reference
// applies).
//
// ReleasePrimary is idempotent and the press state is tracked, so a
// safety release on stop/disconnect never double-fires.
public sealed class MouseInjector
{
    private double _fractionX;
    private double _fractionY;
    private bool _pressed;

    public bool IsPressed => _pressed;

    /// <summary>Win32 error of the last failed SendInput, 0 when none.</summary>
    public int LastError { get; private set; }

    public bool PressPrimary()
    {
        if (_pressed) return true;
        _fractionX = 0;
        _fractionY = 0;
        bool ok = Send(SendInputInterop.MOUSEEVENTF_LEFTDOWN, 0, 0);
        if (ok) _pressed = true;
        return ok;
    }

    public bool ReleasePrimary()
    {
        if (!_pressed) return true;
        _pressed = false;
        return Send(SendInputInterop.MOUSEEVENTF_LEFTUP, 0, 0);
    }

    public bool MoveRelative(double dx, double dy)
    {
        _fractionX += dx;
        _fractionY += dy;

        int wholeX = (int)Math.Truncate(_fractionX);
        int wholeY = (int)Math.Truncate(_fractionY);
        if (wholeX == 0 && wholeY == 0) return true;

        _fractionX -= wholeX;
        _fractionY -= wholeY;
        return Send(SendInputInterop.MOUSEEVENTF_MOVE, wholeX, wholeY);
    }

    private bool Send(uint flags, int dx, int dy)
    {
        var input = new SendInputInterop.MOUSE_INPUT[]
        {
            new()
            {
                type           = SendInputInterop.INPUT_MOUSE,
                mi_dx          = dx,
                mi_dy          = dy,
                mi_dwFlags     = flags,
                mi_dwExtraInfo = SendInputInterop.InjectionTag,
            },
        };

        uint sent = SendInputInterop.SendInput(
            1, input, Marshal.SizeOf<SendInputInterop.MOUSE_INPUT>());
        if (sent == 1)
        {
            LastError = 0;
            return true;
        }

        LastError = Marshal.GetLastWin32Error();
        return false;
    }
}
