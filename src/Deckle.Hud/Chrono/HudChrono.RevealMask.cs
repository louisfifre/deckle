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

    // Latched on the first build exception so UpdateSwipe's retry doesn't throw
    // (and log) every vsync. Cleared by StartSwipe so a fresh take retries.
    private bool _revealsFailed;

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
            // One throw (most likely an alpha-mask / mask-brush type rejection)
            // condemns the whole reveal for this take — same failure mode for
            // every digit — so tear down any partial build and latch, falling
            // back to the flat accent. Recorded so a degraded reveal isn't
            // mistaken for a design choice.
            TearDownReveals();
            _revealsFailed = true;
            DeckleHudSource.Log.RevealMaskFailed(ex.GetType().Name, ex.Message);
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
