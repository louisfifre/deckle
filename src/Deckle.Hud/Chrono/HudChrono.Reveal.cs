using Deckle.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Hud;

// HudChrono — the progressive-coloring swipe reveal.
//
// During Transcribing and Rewriting, a wave travels left→right across the 6
// digits. Each digit carries its own *heat* scalar in [0, 1] driving the
// Opacity of its accent overlay TextBlock — when heat rises, the overlay
// (ChronoAccentBrush) cross-fades in over the primary; at heat=1
// the digit reads as pure accent.
//
// The wave motion math (head walk + asymmetric rise/decay lerp) lives in
// `SwipeWaveAnimator` (Deckle.Composition.Primitives) since 2026-05-02.
// HudChrono drives the animator's Tick() each vsync and copies the per-element
// heat values onto the digits' XAML TextBlock.Opacity. Tunables (cycle, easing,
// rise/decay alphas, head domain) are public statics on SwipeWaveAnimator and
// can be tuned live by the playground. See the type-level comment on
// SwipeWaveAnimator for the full algorithm description.
//
// Dots (DotA / DotB) have no accent overlay and no heat tracking — they stay at
// the at-rest background tone the whole cycle. Unchanged digits (those that did
// not flip during
// Recording, per the animator's changed flags) have their target pinned at 0 so
// their heat only decays; if they inherit any heat from the Recording hand-off,
// it fades and then they stay dark.
//
// Why managed driving instead of CompositionPropertySet + animation: the heat
// state depends on the head index *and* the per-digit changed flag, neither of
// which is cleanly expressible as a Composition Expression. At 6 elements ×
// vsync the per-frame cost is trivial in managed code.
public sealed partial class HudChrono
{
    // Digit count — structural, must match the 6 accent overlays declared
    // in HudChrono.xaml. Not a tunable. Passed to the animator at
    // construction so its internal arrays are sized identically.
    private const int DigitCount = 6;

    // Per-digit changed flags + heat — owned by the SwipeWaveAnimator
    // since 2026-05-02 (canonical extraction into Deckle.Composition).
    // The animator holds two parallel arrays of length DigitCount:
    //   - changed[i] : "was modified during Recording" — preserved across
    //                  the Recording → Transcribing / Rewriting transition
    //                  so the swipe can tell which digits are eligible for
    //                  the accent flash (dots and unchanged digits stay at
    //                  Opacity 0 on their accent overlay forever).
    //   - heat[i]    : 0..1 driving the accent overlay's Opacity. Rises
    //                  fast when the swipe head is on a changed digit,
    //                  decays slowly afterwards. The asymmetric rise/decay
    //                  (see SwipeWaveAnimator.SwipeRiseAlpha /
    //                  SwipeDecayAlpha) gives the wave effect described in
    //                  the spec: a digit keeps glowing for a moment after
    //                  the head has moved on, so several digits are
    //                  partially lit at once — a trailing comet instead of
    //                  a single moving pixel.
    // Index order matches `_digitPrimary` / `_digitAccent`:
    //   0 Min1, 1 Min2, 2 Sec1, 3 Sec2, 4 Cs1, 5 Cs2.
    private readonly SwipeWaveAnimator _swipe = new(DigitCount);

    // Cached references assembled in EnsureSwipeInfra. Parallel arrays so
    // the per-frame loop is a tight zip over three indices. Accent elements
    // are TextBlocks (NOT UIElements in general) so we can assign .Opacity
    // directly without reaching for Composition.
    private TextBlock[]? _digitPrimary;
    private TextBlock[]? _digitAccent;

    private readonly System.Diagnostics.Stopwatch _swipeStopwatch = new();
    private bool _swipeRunning;

    private void EnsureSwipeInfra()
    {
        if (_digitPrimary is null)
        {
            _digitPrimary = new[] { Min1, Min2, Sec1, Sec2, Cs1, Cs2 };
            _digitAccent  = new[] { Min1Accent, Min2Accent, Sec1Accent, Sec2Accent, Cs1Accent, Cs2Accent };
            _cellElements = new FrameworkElement[] { Min1Cell, Min2Cell, Sec1Cell, Sec2Cell, Cs1Cell, Cs2Cell };
        }
    }

    private void StartSwipe()
    {
        EnsureSwipeInfra();
        if (_swipeRunning) return;
        _swipeStopwatch.Restart();
        _swipeRunning = true;
        // The Stop background tone (Tertiary) is already painted by the calling
        // Apply* method. Build the per-digit conic reveals the swipe cross-fades
        // in over that tone; clear the failure latch first so a fresh take
        // retries any that failed last time. Cells whose layout hasn't settled
        // yet are retried from UpdateSwipe.
        _revealsFailed = false;
        EnsureReveals();
    }

    private void StopSwipe()
    {
        if (!_swipeRunning) return;
        _swipeStopwatch.Stop();
        _swipeRunning = false;
        // Tear the conic reveals down before the stroke they borrow from is
        // disposed (StopSwipe precedes DetachProcessingVisual in every path).
        TearDownReveals();
        // Drop heat to zero and hide the accent overlays on the way out.
        // The next state entry (ApplyRecording / ApplyCharging) takes
        // over from a clean slate.
        ClearDigitHeat();
    }

    private void UpdateSwipe()
    {
        if (!_swipeRunning || _digitPrimary is null || _digitAccent is null) return;

        // Advance the animator: it computes new heats given the seconds
        // elapsed since the cycle started, reading the SwipeWaveAnimator
        // statics for cadence / easing / rise-decay alphas.
        _swipe.Tick(_swipeStopwatch.Elapsed.TotalSeconds);

        // Retry building any reveal whose cell wasn't laid out on the
        // synchronous StartSwipe call. Skipped once all six are built.
        if (!_revealsFailed && RevealsPending()) EnsureReveals();

        for (int i = 0; i < DigitCount; i++)
        {
            // Heat only ever rises on a digit the animator flagged "changed"
            // (animated this take); every other stays at 0 and keeps showing the
            // Tertiary background. Rounded to 3 decimals so floating noise
            // (0.9999997) doesn't re-invalidate the render pass every frame.
            double rounded = System.Math.Round(_swipe.GetHeat(i), 3);
            double primaryOpacity = 1.0 - rounded;

            var reveal = _reveals[i];
            if (reveal is not null)
            {
                // Conic reveal: the masked sprite cross-fades the living conic in
                // over the Tertiary primary as heat rises. Keep the flat accent
                // overlay hidden so it can't stack with it.
                reveal.SetHeat((float)rounded);
                if (_digitAccent[i].Opacity != 0)
                    _digitAccent[i].Opacity = 0;
            }
            else
            {
                // Fallback (step 1) until/unless the conic reveal builds: the
                // flat accent overlay cross-fades in instead.
                if (_digitAccent[i].Opacity != rounded)
                    _digitAccent[i].Opacity = rounded;
            }

            // Keep primary + reveal/accent opacity summing to 1 so only one glyph
            // ever inks — otherwise two ClearType copies double up on subpixel
            // coverage and the revealed digit reads bolder (see WriteDigit).
            if (_digitPrimary[i].Opacity != primaryOpacity)
                _digitPrimary[i].Opacity = primaryOpacity;
        }
    }

    // Drops the per-digit "ever-changed" flags. Not called on
    // Transcribing / Rewriting entry — those preserve the flags so the
    // swipe reveal can target only the digits that actually moved.
    private void ClearDigitChanged()
    {
        _swipe.ClearAllChanged();
    }

    // Drops all heat to zero and pushes Opacity=0 onto the accent
    // overlays. Used on state entries that need a clean slate (Charging,
    // Recording start). Transcribing / Rewriting inherit heat from the
    // last Recording frame so the previously-lit digits decay naturally
    // as the swipe picks them up.
    private void ClearDigitHeat()
    {
        _swipe.ClearAllHeat();
        if (_digitAccent is null) return;
        foreach (var t in _digitAccent) t.Opacity = 0;
        // Restore the primary-glyph invariant: accent = 0 ⇒ primary = 1.
        // Without this, primaries knocked to 0 by a previous Recording
        // flash would stay hidden after the state transition clears the
        // accents.
        if (_digitPrimary is null) return;
        foreach (var t in _digitPrimary) t.Opacity = 1;
    }

    // HudPlayground-only: force the "digit was modified during Recording"
    // flags so the swipe reveal (only visible on flagged digits) can be
    // observed without first running a full Recording cycle. Shipping
    // Deckle never calls this — the flags flip naturally inside
    // UpdateClock as the chrono advances.
    //
    // The four CS digits and both seconds digits are the usual candidates
    // for "was modified" because a typical recording spans at least a few
    // tenths of a second. Minutes stay unflagged unless a call supplies
    // true for them explicitly — mirrors the shipping pattern where
    // minutes only flip on recordings longer than 60s.
    public void SimulateChangedDigits(
        bool min1, bool min2,
        bool sec1, bool sec2,
        bool cs1,  bool cs2)
    {
        // Index order matches the animator's flags: 0 Min1, 1 Min2,
        // 2 Sec1, 3 Sec2, 4 Cs1, 5 Cs2.
        _swipe.SetChanged(0, min1);
        _swipe.SetChanged(1, min2);
        _swipe.SetChanged(2, sec1);
        _swipe.SetChanged(3, sec2);
        _swipe.SetChanged(4, cs1);
        _swipe.SetChanged(5, cs2);
        // The changed flags only select which digits the Stop swipe re-lights;
        // the background tone is a uniform Tertiary set by the Apply* method, so
        // there is nothing to repaint here when the Playground flips the flags
        // after StartSwipe.
    }

    // CubicBezierEase moved to Deckle.Composition.Primitives.Easing
    // 2026-05-02 — see Deckle.Composition/Primitives/Easing.cs. Pure
    // math, callers reach it as Easing.CubicBezier(...).
}
