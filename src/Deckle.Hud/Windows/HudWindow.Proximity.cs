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
    // While active, every mouse move recomputes the HUD alpha (UpdateProximity).
    // Subscription is idempotent; each Enable re-seeds the alpha from the
    // current cursor position so a state change reflects reality immediately
    // rather than waiting for the next move.

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

    // ── Proximity: distance → alpha via smoothstep ────────────────────────────

    private void UpdateProximity()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var pos  = AppWindow.Position;
        var size = AppWindow.Size;
        int left   = pos.X;
        int top    = pos.Y;
        int right  = pos.X + size.Width;
        int bottom = pos.Y + size.Height;

        int dx = cursor.X < left ? left - cursor.X : (cursor.X > right  ? cursor.X - right  : 0);
        int dy = cursor.Y < top  ? top  - cursor.Y : (cursor.Y > bottom ? cursor.Y - bottom : 0);
        double distancePx = Math.Sqrt(dx * dx + dy * dy);

        double scale  = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        double nearPx = NEAR_RADIUS_DIP * scale;
        double farPx  = FAR_RADIUS_DIP  * scale;

        double t = (distancePx - nearPx) / (farPx - nearPx);
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;

        double eased = t * t * (3.0 - 2.0 * t);

        byte alpha = (byte)Math.Round(MIN_ALPHA + eased * (MAX_ALPHA - MIN_ALPHA));
        if (alpha != _currentAlpha) SetAlphaImmediate(alpha);

        // Axis 5: sample collection for ProximityRollup. The
        // _proximityRollupEnabled flag short-circuits collection when no
        // listener is attached to Verbose+Heartbeat on Deckle.Hud; this is the
        // strict gate required by the deckle-logging doctrine for
        // high-frequency WM_INPUT loops (~125 Hz). Re-evaluated at the start
        // of each visibility session to absorb a listener live toggle between
        // two shows.
        if (_proximityRollupEnabled)
        {
            int distDip = (int)Math.Round(distancePx / scale);
            _proximityRollup.Add(distDip, alpha);
        }
    }

    // ── Proximity rollup — per-session HUD visibility summary ──────────
    //
    // WM_INPUT arrives at ~125 Hz when the mouse moves; deckle-logging
    // doctrine forbids emitting one event per tick. A previous 1 s periodic
    // variant produced up to ~10 events per HUD session (50 sessions ×
    // ~10 s/day = ~500 events/day in LogWindow) with no diagnostic value on
    // sessions where the mouse did not approach. The current pattern
    // aggregates the full visibility window (shown → hidden) and emits one
    // summary under two cumulative conditions: at least one sample collected
    // AND min_alpha != max_alpha (otherwise smoothstep stayed flat and there
    // is no proximity trajectory to diagnose).

    private void BeginProximitySession()
    {
        // Evaluates the gate at session start; when closed, collection is
        // short-circuited in UpdateProximity (_proximityRollupEnabled test).
        // If a listener attaches during the session, nothing is recorded late;
        // the next show will capture the new gate.
        _proximityRollupEnabled = DeckleHudSource.Log.IsEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat);
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

        // Skip if min == max: the mouse did not enter the proximity radius,
        // smoothstep stayed flat, and there is no trajectory to diagnose. The
        // "every emission carries diagnostic value" doctrine requires this
        // gate, otherwise the LogWindow is drowned in "nothing happened"
        // summaries on typical HUD sessions where the user does not approach
        // the HUD.
        if (minAlpha == maxAlpha) return;

        // Re-test the gate at flush time; a listener may have detached during
        // the session. Matches the double-test semantics of the previous
        // periodic design.
        if (!DeckleHudSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;

        int durationMs = sw is null ? 0 : (int)sw.ElapsedMilliseconds;
        var (p50, p95) = _proximityRollup.ComputePercentiles();

        DeckleHudSource.Log.ProximityRollup(
            durationMs, samples, minAlpha, maxAlpha, p50, p95);
    }
}
