using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Deckle.Composition;

namespace Deckle.Hud;

// HudChrono — digit reveal mask (the processing material in all six digits).
//
// At Stop, each digit's glyph masks one clone cone placed at the HUD apex:
// every digit samples its own slice of that single cone, which rotates on its
// own clone periods, independent of the contour stroke.
//
// One DigitRevealVisual per digit cell, built lazily once layout has settled.
// The glyph mask is the readout TextBlock's own alpha (TextBlock.GetAlphaMask),
// so each masked digit is pixel-identical to the readout. See
// HudComposition.DigitReveal for the material + mask build.
//
// The sprite is parented on the digit's *cell Grid* (sibling of the primary and
// accent TextBlocks), NOT on the primary TextBlock: UIElement.Opacity propagates
// to child visuals, so attaching to the primary, whose Opacity changes with the
// flash, would fade the conic out with it. As a sibling under the cell, the
// sprite's Opacity is driven independently by SetHeat.
//
// Everything degrades cleanly: a cell whose layout hasn't settled is retried
// next tick; repeated build failures latch the whole face onto the flat-accent
// fallback for the take.
public sealed partial class HudChrono
{
    // One reveal per digit, in _digitPrimary order. Null until built.
    private readonly HudComposition.DigitRevealVisual?[] _reveals
        = new HudComposition.DigitRevealVisual?[DigitCount];

    // The clone cone material the six reveals share, built once per take and
    // owned here, not by the stroke.
    private HudComposition.RevealConeMaterial? _revealMaterial;

    private FrameworkElement[]? _cellElements;

    // Latched after MaxRevealBuildAttempts consecutive build failures, so the
    // retry stops throwing and logging every vsync.
    private bool _revealsFailed;
    private int  _revealBuildAttempts;

    // Counted in vsync frames, one build attempt per frame.
    private const int MaxRevealBuildAttempts = 90;

    private bool RevealsPending()
    {
        for (int i = 0; i < _reveals.Length; i++)
            if (_reveals[i] is null) return true;
        return false;
    }

    // Idempotent and retry-safe: skips cells already built and cells whose
    // layout hasn't settled (ActualWidth still 0 on the synchronous StartReveal
    // call). Without a processing stroke, every digit stays on the flat-accent
    // path.
    private void EnsureReveals()
    {
        if (_revealsFailed) return;
        if (_digitPrimary is null || _cellElements is null) return;
        if (_processingStroke is null) return;
        // The reveal cone turns on its own clone rotations, independent of the
        // stroke; the stroke still supplies EffectProps (shared grading) and
        // Config (palette and clone periods).

        // The apex sits at CloneCentre*Fraction · hostSize; (0.5, 0.5)
        // reproduces the contour's centred cone. Same pre-layout fallback size
        // as the stroke factory; in the shipping HUD the host is laid out before
        // the reveal starts.
        float hostW = (float)ProcessingSurfaceHost.ActualWidth;
        float hostH = (float)ProcessingSurfaceHost.ActualHeight;
        if (hostW <= 0f || hostH <= 0f) { hostW = 272f; hostH = 78f; }
        var hostSize = new Vector2(hostW, hostH);
        var cfg  = _processingStroke.Config;
        var apex = new Vector2(
            cfg.CloneCentreXFraction * hostW,
            cfg.CloneCentreYFraction * hostH);

        var compositor = ElementCompositionPreview
            .GetElementVisual(ProcessingSurfaceHost).Compositor;

        try
        {
            _revealMaterial ??= HudComposition.BuildRevealConeMaterial(
                compositor, hostSize, apex, cfg, _animationsEnabled);

            for (int i = 0; i < DigitCount; i++)
            {
                if (_reveals[i] is not null) continue;       // already built

                var cell = _cellElements[i];
                float cw = (float)cell.ActualWidth;
                float ch = (float)cell.ActualHeight;
                if (cw <= 0f || ch <= 0f) continue;          // layout not settled — retry next tick

                // The mask IS the readout glyph's alpha — the frozen digit's
                // exact shape, captured once (the clock is stopped during the
                // reveal, so the glyph won't change underneath it).
                CompositionBrush glyphAlphaMask = _digitPrimary[i].GetAlphaMask();

                // conicCentre = apex − cellOffset places the clone's centre at
                // `apex` in host space.
                Point o = cell.TransformToVisual(ProcessingSurfaceHost)
                              .TransformPoint(new Point(0, 0));
                var conicCentre = apex - new Vector2((float)o.X, (float)o.Y);

                var reveal = HudComposition.CreateDigitReveal(
                    compositor,
                    _processingStroke,
                    _revealMaterial,
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
            // digit) and retry on the next vsync: the reveal can start on the
            // tick a glyph is rebuilt, before its alpha mask is rasterised, and
            // GetAlphaMask then throws for a frame or two. Only after
            // MaxRevealBuildAttempts consecutive failures, latch the flat-accent
            // fallback and log once, so a genuine rejection is still recorded.
            TearDownReveals();
            if (++_revealBuildAttempts >= MaxRevealBuildAttempts)
            {
                _revealsFailed = true;
                DeckleHudSource.Log.RevealMaskFailed();
                DeckleHudSource.Log.RevealMaskFailedDetail(ex.GetType().Name, ex.Message);
            }
        }
    }

    // Detach + dispose every built reveal. Always runs before the stroke's own
    // Dispose (StopReveal precedes DetachProcessingVisual in every exit path), so
    // the shared surface / PropertySets the reveals reference are still alive.
    // Latch-free on purpose: the failure latch is owned by EnsureReveals (which
    // calls this on its own failure path) and cleared by StartReveal.
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
        // The shared clone material (surfaces + rotations) outlives the per-digit
        // brushes that bound it — dispose it only after every reveal above is
        // gone, or a brush / rotation expression would touch freed memory on its
        // last render tick.
        _revealMaterial?.Dispose();
        _revealMaterial = null;
    }
}
