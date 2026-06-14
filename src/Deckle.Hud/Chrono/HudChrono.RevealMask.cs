using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Deckle.Composition;

namespace Deckle.Hud;

// HudChrono — digit reveal mask (the swipe's conic material, all six digits).
//
// Step 2 of the Stop swipe: instead of cross-fading the flat accent overlay,
// each animated digit's glyph masks the SAME living conic the processing stroke
// samples — digit and contour as windows on one material (F1: one conic behind
// the whole HUD, sampled at each element's HUD position).
//
// One DigitRevealVisual per digit cell, built lazily once layout has settled.
// The glyph mask is the readout TextBlock's own alpha (TextBlock.GetAlphaMask),
// so each masked digit is pixel-identical to the readout — no Win2D font
// handling at all. See HudComposition.DigitReveal for the material + mask build.
//
// The sprite is parented on the digit's *cell Grid* (sibling of the primary and
// accent TextBlocks), NOT on the primary TextBlock: UIElement.Opacity propagates
// to child visuals, so attaching to the primary — whose Opacity the swipe drives
// to 1-heat — would fade the conic out with it. As a sibling under the cell, the
// sprite's Opacity is driven independently by SetHeat.
//
// Everything is additive and degrades cleanly: a cell whose layout hasn't
// settled is retried next tick; a build exception latches and the whole face
// falls back to the flat-accent path (step 1) for the take.
public sealed partial class HudChrono
{
    // One reveal per digit (index order matches _digitPrimary and the animator:
    // 0 Min1 … 5 Cs2). Null until built, or after a build failure latches.
    private readonly HudComposition.DigitRevealVisual?[] _reveals
        = new HudComposition.DigitRevealVisual?[DigitCount];

    // The six cell Grids hosting each digit's primary + accent TextBlocks; the
    // conic sprite is parented here. Assembled in EnsureSwipeInfra.
    private FrameworkElement[]? _cellElements;

    // Latched once the reveal build has failed too many times in a row, so
    // UpdateSwipe's retry stops throwing (and logging) every vsync. NOT latched
    // on the first failure: in the Playground the swipe can start the very tick a
    // digit's glyph is (re)built, before its alpha mask is rasterised, so
    // GetAlphaMask throws on the first attempt — which used to condemn the whole
    // take to the flat-accent fallback. We now retry for a short window and latch
    // only if it keeps failing. Both reset by StartSwipe for a fresh take.
    private bool _revealsFailed;
    private int  _revealBuildAttempts;

    // Consecutive failed build attempts tolerated before giving up (≈ the retry
    // window in vsync frames). A transient "glyph not ready yet" miss costs a few
    // retries; a genuine type rejection latches after this many and logs once.
    private const int MaxRevealBuildAttempts = 90;

    // True while any cell still lacks its reveal — lets UpdateSwipe skip the
    // host-geometry reads in EnsureReveals once all six are built.
    private bool RevealsPending()
    {
        for (int i = 0; i < _reveals.Length; i++)
            if (_reveals[i] is null) return true;
        return false;
    }

    // Build the per-digit conic reveals sharing the live stroke's material.
    // Idempotent and retry-safe: skips cells already built, and cells whose
    // layout hasn't settled (ActualWidth still 0 on the synchronous StartSwipe
    // call). Skips entirely if the stroke or its rotation material is absent,
    // leaving every digit on the flat-accent path.
    private void EnsureReveals()
    {
        if (_revealsFailed) return;
        if (_digitPrimary is null || _cellElements is null) return;
        if (_processingStroke is null) return;
        // No shared rotation to bind to (frozen-hue stroke) ⇒ no living conic to
        // reveal. Shouldn't happen in Transcribing / Rewriting (both spin).
        if (_processingStroke.HueRotationProps is null) return;

        // Host size the stroke's conic is centred on (same fallback the stroke
        // factory uses pre-layout). The conic surface centre maps to hostSize/2.
        float hostW = (float)ProcessingSurfaceHost.ActualWidth;
        float hostH = (float)ProcessingSurfaceHost.ActualHeight;
        if (hostW <= 0f || hostH <= 0f) { hostW = 272f; hostH = 78f; }
        var hostCentre = new Vector2(hostW / 2f, hostH / 2f);

        var compositor = ElementCompositionPreview
            .GetElementVisual(ProcessingSurfaceHost).Compositor;

        try
        {
            for (int i = 0; i < DigitCount; i++)
            {
                if (_reveals[i] is not null) continue;       // already built

                var cell = _cellElements[i];
                float cw = (float)cell.ActualWidth;
                float ch = (float)cell.ActualHeight;
                if (cw <= 0f || ch <= 0f) continue;          // layout not settled — retry next tick

                // The mask IS the readout glyph's alpha — the frozen digit's
                // exact shape, captured once (the clock is stopped during the
                // swipe, so the glyph won't change underneath it).
                CompositionBrush glyphAlphaMask = _digitPrimary[i].GetAlphaMask();

                // Offset of this cell's top-left within the conic's host space;
                // conicCentre = hostCentre − cellOffset places surface-pixel
                // (x,y) at the SAME HUD point the stroke shows there — one shared
                // conic across the row, not a private one per digit (F1).
                Point o = cell.TransformToVisual(ProcessingSurfaceHost)
                              .TransformPoint(new Point(0, 0));
                var conicCentre = hostCentre - new Vector2((float)o.X, (float)o.Y);

                var reveal = HudComposition.CreateDigitReveal(
                    compositor,
                    _processingStroke,
                    glyphAlphaMask,
                    new Vector2(cw, ch),
                    conicCentre);

                ElementCompositionPreview.SetElementChildVisual(cell, reveal.Visual);
                _reveals[i] = reveal;

                DeckleHudSource.Log.RevealGeometry(cw, ch, o.X, o.Y, hostW, hostH);
            }
        }
        catch (System.Exception ex)
        {
            // Tear down any partial build (one throw fails the same way for every
            // digit) and retry on the next vsync — the most common cause in the
            // Playground is a glyph whose alpha mask isn't rasterised yet on the
            // tick the swipe starts, which clears itself a frame or two later.
            // Only after MaxRevealBuildAttempts consecutive failures do we give
            // up: latch the flat-accent fallback and log once, so a genuine type
            // rejection is still recorded and not mistaken for a design choice.
            TearDownReveals();
            if (++_revealBuildAttempts >= MaxRevealBuildAttempts)
            {
                _revealsFailed = true;
                DeckleHudSource.Log.RevealMaskFailed(ex.GetType().Name, ex.Message);
            }
        }
    }

    // Detach + dispose every built reveal. Always runs before the stroke's own
    // Dispose (StopSwipe precedes DetachProcessingVisual in every exit path), so
    // the shared surface / PropertySets the reveals reference are still alive.
    // Latch-free on purpose: the failure latch is owned by EnsureReveals (which
    // calls this on its own failure path) and cleared by StartSwipe.
    private void TearDownReveals()
    {
        for (int i = 0; i < _reveals.Length; i++)
        {
            if (_reveals[i] is null) continue;
            if (_cellElements is not null)
                ElementCompositionPreview.SetElementChildVisual(_cellElements[i], null);
            _reveals[i]!.Dispose();
            _reveals[i] = null;
        }
    }
}
