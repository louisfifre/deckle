using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// HudWindow — fade-in animation on the Hidden → visible transition.
public sealed partial class HudWindow : Window
{
    // ── Fade-in: Hidden → visible transition ──────────────────────────────────
    //
    // Proximity is suspended for the duration so a cursor move mid-fade cannot
    // snap alpha to a smoothstep value while the fade-in is still ramping up.

    // True between FadeInStarted and its FadeInEnded, so every exit path
    // closes the fade it interrupts exactly once.
    private bool _fadeInPending;

    private void StartFadeIn(byte target, bool activateProximityOnComplete)
    {
        _fadeInTimer?.Stop();
        EndFadeIn("cancelled");
        DisableProximity();
        byte fromAlpha = _currentAlpha;
        SetAlphaImmediate(0);
        _fadeInTarget = target;
        _fadeInActivateProximityOnComplete = activateProximityOnComplete;
        _fadeInStartUtc = DateTime.UtcNow;
        _fadeInTimer ??= DispatcherQueue.CreateTimer();
        _fadeInTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeInTimer.IsRepeating = true;
        _fadeInTimer.Tick -= OnFadeInTick;
        _fadeInTimer.Tick += OnFadeInTick;
        _fadeInTimer.Start();

        // fromAlpha is captured before the reset to 0 to trace a fade that
        // starts from an in-progress proximity alpha.
        DeckleHudSource.Log.FadeInStarted("hud", FADE_IN_MS, fromAlpha, target);
        _fadeInPending = true;
    }

    private void EndFadeIn(string outcome)
    {
        if (!_fadeInPending) return;
        _fadeInPending = false;
        int elapsedMs = (int)(DateTime.UtcNow - _fadeInStartUtc).TotalMilliseconds;
        DeckleHudSource.Log.FadeInEnded("hud", outcome, elapsedMs, _currentAlpha);
    }

    private void OnFadeInTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var elapsed = (DateTime.UtcNow - _fadeInStartUtc).TotalMilliseconds;
        var t = Math.Clamp(elapsed / FADE_IN_MS, 0.0, 1.0);

        var oneMinusT = 1.0 - t;
        var eased = 1.0 - (oneMinusT * oneMinusT * oneMinusT);

        var alpha = (byte)Math.Clamp(Math.Round(_fadeInTarget * eased), 0, 255);
        SetAlphaImmediate(alpha);

        if (t >= 1.0)
        {
            sender.Stop();
            SetAlphaImmediate(_fadeInTarget);
            EndFadeIn("completed");
            if (_fadeInActivateProximityOnComplete &&
                Settings.SettingsService.Instance.Current.Overlay.FadeOnProximity)
            {
                EnableProximity();
            }
        }
    }

    private void CancelFadeIn()
    {
        _fadeInTimer?.Stop();
        EndFadeIn("cancelled");
    }

    private void CompleteFadeInImmediately()
    {
        if (_fadeInTimer?.IsRunning != true) return;

        _fadeInTimer.Stop();
        SetAlphaImmediate(_fadeInTarget);
        EndFadeIn("completed");
        if (_fadeInActivateProximityOnComplete &&
            Settings.SettingsService.Instance.Current.Overlay.FadeOnProximity)
        {
            EnableProximity();
        }
    }
}
