using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Deckle.Composition;

// ProcessingVariant moved to Deckle.Composition module 2026-05-02 — see
// Core/ProcessingVariant.cs. Same namespace (Deckle.Composition) so
// in-file references resolve unchanged via the cross-assembly project
// reference.

// HUD Composition pipeline — processing strokes for the chrono surface.
//
// Strictly internal: every visual produced here stays inside the 272x78 HUD
// rect. The HUD window runs with WS_EX_LAYERED for proximity fade (which
// disables the DWM shell shadow) and the rect is a tight fit around the
// card — any external DropShadow would clip at the HWND edge, producing a
// rectangular artifact. No shadows in this pipeline.
//
// Geometry is flush with the card edge (InsetDip = 0). CornerRadius 7 dip
// sits just inside the DWM 8-dip rounded silhouette, so the Win2D-rasterised
// stroke clears the DWM corner clip even though the two AA pipelines don't
// agree at high-curvature arcs.
//
// Pixel-perfect sizing note (CreateConicArcStroke): the stroke silhouette
// surface is dimensioned with Math.Round of the visual DIP extent, NOT
// Math.Ceiling. At non-integer DPI (e.g. 125 % gives hostSize.Y = 78.4),
// ceiling would oversize the surface by up to 1 pixel (pxH = 79 for a
// 78.4-dip visual). CompositionSurfaceBrush.Stretch = Fill then compresses
// 79 source rows into 78.4 dip — scale 0.9924 — so the stroke's outer edge
// drawn at source y = pxH lands at visual y = 77.41 instead of 78.4. That
// 1-dip gap is the stroke "disappearing" asymmetrically on the bottom/right
// edges (top/left are pinned at y=0/x=0 by the origin, so they stay flush).
// Math.Round gets the surface size within ±0.5 dip of innerSize on every
// side, and Stretch.Fill then stretches (scale ≥ ~1) to land the outer
// stroke edge flush with the visual extent on all four sides. pxSquare
// (rotation coverage) is computed from innerSize directly, not from the
// rounded pxW/pxH, so it always clears the visual diagonal.
public static partial class HudComposition
{
    // ╔════════════════════════════════════════════════════════════════════╗
    // ║  Shared geometry                                                   ║
    // ╚════════════════════════════════════════════════════════════════════╝
    // Fixed across all three variants — stroke metrics are a property of
    // the HUD rect, not of the animation.
    private const float  StrokeThickness              = 4f;    // dip, stroke width
    // `public static` (not const) — HudPlayground tunes the inset live to
    // explore stroke geometry without rebuilding the app. Shipping code
    // still reads it as if it were a const: the field reads inline cleanly
    // when nothing mutates it in a given process. Mutating live requires
    // rebuilding the stroke (paint-time geometry); the playground triggers
    // that via its existing rebuild path.
    public  static       float InsetDip                = -2f;  // dip, inset from HUD edge
    private const float  CornerRadiusDip               = 8f;   // dip, rounded-rect corner radius

}
