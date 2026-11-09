using System.Runtime.InteropServices;
using Deckle.Core;
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
// A press can come after the cursor already moved (MarkPressOrigin, then
// moves, then PressPrimary). The press then has to land where the motion
// began, and relative moves cannot take the cursor back: the ballistics
// make the return trip differ from the way out. So that press is one
// SendInput batch of three — an absolute move to the origin, the button,
// an absolute move back to where the cursor is. One batch, because
// SendInput inserts a batch whole; SetCursorPos around a separate press
// could run before the input thread delivers the press, which would then
// land at the wrong point.
//
// ReleasePrimary is idempotent and the press state is tracked, so a
// safety release on stop/disconnect never double-fires.
public sealed class MouseInjector
{
    private double _fractionX;
    private double _fractionY;
    private bool _pressed;
    private POINT? _pressOrigin;

    public bool IsPressed => _pressed;

    public int LastError { get; private set; }

    /// <summary>
    /// Records where the cursor stands now, so the next PressPrimary lands
    /// there even after moves. Without a readable cursor, the press falls
    /// back to wherever the cursor is when it fires.
    /// </summary>
    public void MarkPressOrigin()
    {
        _fractionX = 0;
        _fractionY = 0;
        _pressOrigin = NativeMethods.GetCursorPos(out POINT origin) ? origin : null;
    }

    public void ClearPressOrigin() => _pressOrigin = null;

    public bool PressPrimary()
    {
        if (_pressed) return true;

        POINT? origin = _pressOrigin;
        _pressOrigin = null;

        if (origin is POINT o && NativeMethods.GetCursorPos(out POINT current))
            return PressAt(o, current);

        _fractionX = 0;
        _fractionY = 0;
        bool ok = Send(SendInputInterop.MOUSEEVENTF_LEFTDOWN, 0, 0);
        if (ok) _pressed = true;
        return ok;
    }

    public bool ReleasePrimary()
    {
        _pressOrigin = null;
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

    // The fractional remainder is kept: it belongs to the motion already
    // under way, which the press does not interrupt.
    private bool PressAt(POINT origin, POINT current)
    {
        var batch = new[]
        {
            AbsoluteMove(origin),
            Input(SendInputInterop.MOUSEEVENTF_LEFTDOWN, 0, 0),
            AbsoluteMove(current),
        };

        uint sent = SendInputInterop.SendInput(
            (uint)batch.Length, batch, Marshal.SizeOf<SendInputInterop.MOUSE_INPUT>());

        // Two events in means the button went down, so a release is owed
        // even if the return move was refused.
        if (sent >= 2) _pressed = true;
        if (sent == batch.Length)
        {
            LastError = 0;
            return true;
        }

        LastError = Marshal.GetLastWin32Error();
        return false;
    }

    // Absolute coordinates are normalized to 0..65535 across the virtual
    // desktop, every monitor included. GetCursorPos and the metrics both
    // speak physical pixels in a per-monitor-aware process.
    private static SendInputInterop.MOUSE_INPUT AbsoluteMove(POINT point)
    {
        int left   = SendInputInterop.GetSystemMetrics(SendInputInterop.SM_XVIRTUALSCREEN);
        int top    = SendInputInterop.GetSystemMetrics(SendInputInterop.SM_YVIRTUALSCREEN);
        int width  = SendInputInterop.GetSystemMetrics(SendInputInterop.SM_CXVIRTUALSCREEN);
        int height = SendInputInterop.GetSystemMetrics(SendInputInterop.SM_CYVIRTUALSCREEN);

        int x = (int)Math.Round((point.X - left) * 65535.0 / Math.Max(1, width - 1));
        int y = (int)Math.Round((point.Y - top) * 65535.0 / Math.Max(1, height - 1));

        return Input(
            SendInputInterop.MOUSEEVENTF_MOVE
                | SendInputInterop.MOUSEEVENTF_ABSOLUTE
                | SendInputInterop.MOUSEEVENTF_VIRTUALDESK,
            x, y);
    }

    private static SendInputInterop.MOUSE_INPUT Input(uint flags, int dx, int dy) => new()
    {
        type           = SendInputInterop.INPUT_MOUSE,
        mi_dx          = dx,
        mi_dy          = dy,
        mi_dwFlags     = flags,
        mi_dwExtraInfo = SendInputInterop.InjectionTag,
    };

    private bool Send(uint flags, int dx, int dy)
    {
        var input = new[] { Input(flags, dx, dy) };

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
