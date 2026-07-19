using System;
using Microsoft.UI.Dispatching;
using Deckle.Core;
using Deckle.Settings;

namespace Deckle.Hud;

// Window-level animators used by HudOverlayManager to slide and fade overlay
// cards. Both run on a DispatcherQueueTimer ticking at ~60 fps, 150 ms total,
// cubic ease-out (1 - (1-t)^3). Either can be cancelled mid-flight; re-calling
// while an animation is in progress restarts from the current interpolated
// value so transitions collapse instead of queueing.
//
// WindowSlideAnimator is functional HUD motion and follows
// Settings.Overlay.Animations. LayeredAlphaAnimator is a simple fade and
// follows UISettings.AnimationsEnabled through SystemAnimationPreference.

// Slides an HWND from its tracked position to a target via SetWindowPos.
// The animator owns the canonical position — callers should never bypass it
// with a direct SetWindowPos, or the next SlideTo will interpolate from a
// stale starting point.
internal sealed class WindowSlideAnimator
{
    private const int FrameIntervalMs = 16;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _timer;

    private int _currentX;
    private int _currentY;
    private int _fromX;
    private int _fromY;
    private int _toX;
    private int _toY;

    private DateTime _startUtc;
    private Action? _onComplete;
    private bool _isAnimating;
    private readonly AnimationGate _gate;

    public WindowSlideAnimator(
        IntPtr hwnd,
        DispatcherQueue dispatcherQueue,
        int initialX,
        int initialY,
        bool animationsEnabled)
    {
        _hwnd = hwnd;
        _currentX = initialX;
        _currentY = initialY;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(FrameIntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _gate = new AnimationGate(animationsEnabled);
        _gate.Disabled += CompleteImmediately;
    }

    public int CurrentX => _currentX;
    public int CurrentY => _currentY;

    public void SlideTo(int toX, int toY, bool instant = false, Action? onComplete = null)
    {
        if (toX == _currentX && toY == _currentY)
        {
            Cancel();
            onComplete?.Invoke();
            return;
        }

        if (instant || !_gate.IsEnabled)
        {
            Cancel();
            ApplyPosition(toX, toY);
            onComplete?.Invoke();
            return;
        }

        _fromX = _currentX;
        _fromY = _currentY;
        _toX = toX;
        _toY = toY;
        _startUtc = DateTime.UtcNow;
        _onComplete = onComplete;
        _isAnimating = true;

        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _isAnimating = false;
        _onComplete = null;
    }

    public void SetAnimationsEnabled(bool animationsEnabled)
        => _gate.SetEnabled(animationsEnabled);

    private void CompleteImmediately()
    {
        if (!_isAnimating) return;

        _timer.Stop();
        _isAnimating = false;
        ApplyPosition(_toX, _toY);
        var callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    private void ApplyPosition(int x, int y)
    {
        _currentX = x;
        _currentY = y;
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero,
            x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var elapsed = (DateTime.UtcNow - _startUtc).TotalMilliseconds;
        var t = Math.Clamp(elapsed / Duration.TotalMilliseconds, 0.0, 1.0);

        var oneMinusT = 1.0 - t;
        var eased = 1.0 - (oneMinusT * oneMinusT * oneMinusT);

        var x = (int)Math.Round(_fromX + (_toX - _fromX) * eased);
        var y = (int)Math.Round(_fromY + (_toY - _fromY) * eased);

        ApplyPosition(x, y);

        if (t >= 1.0)
        {
            _timer.Stop();
            _isAnimating = false;
            var cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }
    }
}

// Ramps the layered alpha of a WS_EX_LAYERED window via
// SetLayeredWindowAttributes(LWA_ALPHA). Used for overlay card fade-in on show
// and fade-out just before ForceClose.
internal sealed class LayeredAlphaAnimator
{
    private const int FrameIntervalMs = 16;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _timer;

    private byte _currentAlpha;
    private byte _fromAlpha;
    private byte _toAlpha;

    private DateTime _startUtc;
    private Action? _onComplete;
    private bool _isAnimating;
    private readonly AnimationGate _gate;

    public LayeredAlphaAnimator(IntPtr hwnd, DispatcherQueue dispatcherQueue, byte initialAlpha)
    {
        _hwnd = hwnd;
        _currentAlpha = initialAlpha;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(FrameIntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _gate = new AnimationGate(SystemAnimationPreference.Instance.AnimationsEnabled);
        _gate.Disabled += CompleteImmediately;
    }

    public byte CurrentAlpha => _currentAlpha;

    public void FadeTo(byte targetAlpha, bool instant = false, Action? onComplete = null)
    {
        if (targetAlpha == _currentAlpha)
        {
            Cancel();
            onComplete?.Invoke();
            return;
        }

        if (instant || !_gate.IsEnabled)
        {
            Cancel();
            ApplyAlpha(targetAlpha);
            onComplete?.Invoke();
            return;
        }

        _fromAlpha = _currentAlpha;
        _toAlpha = targetAlpha;
        _startUtc = DateTime.UtcNow;
        _onComplete = onComplete;
        _isAnimating = true;

        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _isAnimating = false;
        _onComplete = null;
    }

    public void SetAnimationsEnabled(bool animationsEnabled)
        => _gate.SetEnabled(animationsEnabled);

    private void CompleteImmediately()
    {
        if (!_isAnimating) return;

        _timer.Stop();
        _isAnimating = false;
        ApplyAlpha(_toAlpha);
        var callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    private void ApplyAlpha(byte alpha)
    {
        _currentAlpha = alpha;
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var elapsed = (DateTime.UtcNow - _startUtc).TotalMilliseconds;
        var t = Math.Clamp(elapsed / Duration.TotalMilliseconds, 0.0, 1.0);

        var oneMinusT = 1.0 - t;
        var eased = 1.0 - (oneMinusT * oneMinusT * oneMinusT);

        var alpha = (byte)Math.Clamp(
            Math.Round(_fromAlpha + (_toAlpha - _fromAlpha) * eased),
            0, 255);

        ApplyAlpha(alpha);

        if (t >= 1.0)
        {
            _timer.Stop();
            _isAnimating = false;
            var cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }
    }
}
