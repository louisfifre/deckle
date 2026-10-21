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

// HudWindow — cursor-proximity alpha (distance → smoothstep) and the
// per-session proximity rollup lifecycle.
public sealed partial class HudWindow : Window
{
    // ── Proximity: subscribe to the shared cursor signal ──────────────────────
    //
    // Each Enable re-seeds the alpha from the current cursor position so a
    // state change reflects reality immediately rather than waiting for the
    // next move.

    private void EnableProximity()
    {
        if (!_proximityActive)
        {
            _proximityActive = true;
            _cursorSignal.Moved += UpdateProximity;
        }
        UpdateProximity();
    }

    private void DisableProximity()
    {
        if (!_proximityActive) return;
        _proximityActive = false;
        _cursorSignal.Moved -= UpdateProximity;
    }

    // ── Proximity: distance → alpha via smoothstep (the curve lives in ProximityFade) ──

    private void UpdateProximity()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var pos  = AppWindow.Position;
        var size = AppWindow.Size;
        int left   = pos.X;
        int top    = pos.Y;
        int right  = pos.X + size.Width;
        int bottom = pos.Y + size.Height;

        double distancePx = ProximityFade.DistanceToRectPx(cursor.X, cursor.Y, left, top, right, bottom);
        double scale = _dpiScale;
        byte alpha = ProximityFade.Alpha(distancePx, scale, NEAR_RADIUS_DIP, FAR_RADIUS_DIP, MIN_ALPHA, MAX_ALPHA);
        if (alpha != _currentAlpha) SetAlphaImmediate(alpha);

        // Collected only when the rollup was admitted at session start: this
        // runs on every cursor move.
        if (_proximityRollupEnabled)
        {
            int distDip = (int)Math.Round(distancePx / scale);
            _proximityRollup.Add(distDip, alpha);
        }
    }

    // ── Proximity rollup — per-session HUD visibility summary ──────────
    //
    // Never one event per cursor move: one summary per visibility (shown →
    // hidden), and only when there is a proximity trajectory, at least one
    // sample and min_alpha != max_alpha.

    private void BeginProximitySession()
    {
        // A listener that attaches mid-session is picked up at the next show.
        // Asked through the Windowing admission, not the bare listener level:
        // the always-on journal listener enables every level, which would
        // otherwise arm this per-move collection on every show.
        _proximityRollupEnabled = DeckleHudSource.Log.IsHudDetailEnabled(
            (EventKeywords)Keywords.Heartbeat);
        if (!_proximityRollupEnabled) return;

        _proximityRollup.Reset();
        _proximitySessionStopwatch = System.Diagnostics.Stopwatch.StartNew();
    }

    private void EndProximitySessionAndFlush()
    {
        if (!_proximityRollupEnabled) return;

        var sw = _proximitySessionStopwatch;
        _proximitySessionStopwatch = null;
        _proximityRollupEnabled = false;

        int samples = _proximityRollup.TotalSamples;
        if (samples == 0) return;

        byte minAlpha = _proximityRollup.MinAlpha;
        byte maxAlpha = _proximityRollup.MaxAlpha;

        // The cursor never entered the proximity radius: nothing to diagnose.
        if (minAlpha == maxAlpha) return;

        // A listener may have detached, or admission closed, during the session.
        if (!DeckleHudSource.Log.IsHudDetailEnabled((EventKeywords)Keywords.Heartbeat)) return;

        int durationMs = sw is null ? 0 : (int)sw.ElapsedMilliseconds;
        var (p50, p95) = _proximityRollup.ComputePercentiles();

        DeckleHudSource.Log.ProximityRollup(
            durationMs, samples, minAlpha, maxAlpha, p50, p95);
    }
}
