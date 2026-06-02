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

public static partial class HudComposition
{
    // Full-circle angular gradient surface. pxSquare × pxSquare,
    // painted per pixel: each pixel's colour is OKLCh(OklchLightness,
    // OklchChroma, hue) where hue = (atan2(dy, dx) / 2π) * HueRange
    // + HueStart, optionally quantised into WedgeCount posterised
    // sectors. Shared between CreateConicArcStroke (the shipping
    // stroke) and CreateNakedMaskPreview (dev diagnostic).
    //
    // Why per-pixel instead of rasterising WedgeCount triangles —
    // the old approach drew 360 CanvasGeometry polygons whose shared
    // edges meet at subpixel positions all the way down to the
    // centre. Even with D2D's antialiasing, the triangle boundaries
    // each produce a small coverage error; summed over 360 seams
    // fanning out from the centre, the errors align into a visible
    // radial moiré (the "grid pattern" first spotted on the baked
    // palette screenshot). Per-pixel evaluation has no
    // polygon seams — every pixel is an independent atan2 sample —
    // so the gradient comes out perfectly smooth.
    //
    // Cost: pxSquare² OklchToRgb calls at bake time (≈74 k calls for
    // 272² — a few ms on a cold cache). Paint runs once per stroke
    // creation (variant boundary cross or playground config change),
    // never per frame, so this is immaterial at runtime.
    private static CompositionDrawingSurface PaintConicSurface(
        CanvasDevice canvasDevice,
        CompositionGraphicsDevice graphicsDevice,
        int pxSquare,
        ConicArcStrokeConfig cfg)
    {
        var surface = graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(pxSquare, pxSquare),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        // BGRA premultiplied layout — matches the surface format above.
        // WedgeCount doubles as a posterisation knob: at 360 (default)
        // on a 272-pixel circle each wedge is ~0.76 px wide → reads as
        // continuous; at 12/24 it snaps into visible retro sectors.
        var bytes       = new byte[pxSquare * pxSquare * 4];
        float centre    = pxSquare / 2f;
        int   wedges    = Math.Max(1, cfg.WedgeCount);
        float wedgeStep = MathF.Tau / wedges;

        // Pre-compute the wedge palette once. Each pixel only needs
        // atan2 + a table lookup, instead of a full OKLCh conversion
        // per pixel — cuts bake time from ~15 ms to ~2 ms at 272².
        var palette = new byte[wedges * 3];
        for (int i = 0; i < wedges; i++)
        {
            float centreTurns = (i + 0.5f) / wedges;
            float hue = cfg.HueStart + centreTurns * cfg.HueRange;
            var c = ColorSpace.OklchToRgb(cfg.OklchLightness, cfg.OklchChroma, hue);
            int p = i * 3;
            palette[p + 0] = c.B;
            palette[p + 1] = c.G;
            palette[p + 2] = c.R;
        }

        for (int y = 0; y < pxSquare; y++)
        {
            float dy = y + 0.5f - centre;
            for (int x = 0; x < pxSquare; x++)
            {
                float dx = x + 0.5f - centre;

                // atan2 yields [-π, π]; shift to [0, 2π) so the wedge
                // index below stays in positive angle space.
                float ang = MathF.Atan2(dy, dx);
                if (ang < 0) ang += MathF.Tau;

                int wi = (int)(ang / wedgeStep);
                if (wi >= wedges) wi = wedges - 1;

                int idx = (y * pxSquare + x) * 4;
                int p   = wi * 3;
                bytes[idx + 0] = palette[p + 0];
                bytes[idx + 1] = palette[p + 1];
                bytes[idx + 2] = palette[p + 2];
                bytes[idx + 3] = 0xFF;
            }
        }

        // CanvasBitmap.CreateFromBytes takes the Windows.Graphics.DirectX
        // pixel-format enum; the file's `using Microsoft.Graphics.DirectX`
        // resolves the unqualified name to Microsoft's homonym, which is
        // what CompositionDrawingSurface expects above. Fully-qualify
        // here to route the literal to the right overload.
        using var bitmap = CanvasBitmap.CreateFromBytes(
            canvasDevice, bytes, pxSquare, pxSquare,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        using var ds = CanvasComposition.CreateDrawingSession(surface);
        ds.Clear(Colors.Transparent);
        ds.DrawImage(bitmap);

        return surface;
    }

    // Arc-shaped alpha mask surface — single span in [0, 2π·ConicSpanTurns]
    // with lead/tail alpha ramps governed by ConicLeadFadeTurns,
    // ConicTailFadeTurns, ConicFadeCurve; optionally mirrored at +π when
    // ArcMirror is set. Full white, straight-alpha. Shared with
    // CreateNakedMaskPreview so the dev rail sees the exact same fade
    // geometry the shipping stroke uses.
    //
    // Why per-pixel (not polygonal wedges like the old code) — same
    // reason as PaintConicSurface: rasterising WedgeCount triangles that
    // share a vertex at the centre produces a radial moiré at high wedge
    // counts, because D2D's antialiased coverage at each shared seam is a
    // tiny bit off-1 and the errors stack along every fan ray. That moiré
    // was invisible on the Conic-only preview once that path went
    // per-pixel, but it reappeared whenever the ArcMask was composited in
    // (Rewriting, Combined, and the shipping stroke for Transcribing /
    // Rewriting / Recording — all of which route through AlphaMaskEffect
    // with Arc as the mask). Per-pixel eliminates the polygon seams
    // entirely; alpha is computed independently at each pixel from its
    // own atan2 angle, with the same lead / tail / curve / mirror
    // semantics the polygonal path had.
    //
    // Bonus: no CanvasGeometry.CreatePolygon calls, which removes the
    // degenerate-triangle edge case (near-colinear vertices at high
    // WedgeCount) that Win2D can throw on.
    // `fillColor` — premultiplied RGB written alongside the coverage alpha.
    // Shipping passes Colors.White because the downstream AlphaMaskEffect
    // only reads .A (colour is invisible to the stroke's masking stage).
    // The playground's Naked rail passes a theme-aware opaque colour
    // (black on light, white on dark) so ArcMask and ArcMask-only overlays
    // are legible against LayerFillColorDefaultBrush in both themes —
    // without a colour knob the white-on-alpha mask vanished on light.
    private static CompositionDrawingSurface PaintArcMaskSurface(
        CanvasDevice canvasDevice,
        CompositionGraphicsDevice graphicsDevice,
        int pxSquare,
        ConicArcStrokeConfig cfg,
        Color fillColor)
    {
        var surface = graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(pxSquare, pxSquare),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        // Mirror: max Span is 0.5 so the two arcs can't overlap.
        // Without mirror: Span can go up to 1 (full ring).
        float maxSpanTurns  = cfg.ArcMirror ? 0.5f : 1f;
        float spanTurns     = Math.Clamp(cfg.ConicSpanTurns, 0f, maxSpanTurns);
        float leadFadeTurns = Math.Clamp(cfg.ConicLeadFadeTurns, 0f, spanTurns);
        float tailFadeTurns = Math.Clamp(cfg.ConicTailFadeTurns, 0f, spanTurns);
        // If the two fades would overlap past the span, scale both so
        // they just meet at the mid of the arc (bell shape, no solid
        // core). Otherwise preserve the user-requested lengths.
        float totalFadeTurns = leadFadeTurns + tailFadeTurns;
        if (totalFadeTurns > spanTurns && totalFadeTurns > 0f)
        {
            float scale = spanTurns / totalFadeTurns;
            leadFadeTurns *= scale;
            tailFadeTurns *= scale;
        }
        float spanRadians      = MathF.Tau * spanTurns;
        float leadFadeRadians  = MathF.Tau * leadFadeTurns;
        float tailFadeRadians  = MathF.Tau * tailFadeTurns;
        float tailStartRadians = spanRadians - tailFadeRadians;
        float curve            = MathF.Max(0.01f, cfg.ConicFadeCurve);
        bool  mirror           = cfg.ArcMirror;

        // Early-out for a degenerate span (no arc visible). Return the
        // empty transparent surface rather than iterating the full grid
        // with alpha=0 — saves ~3 ms on 272² at no visual cost.
        if (spanRadians <= 0f)
            return surface;

        var bytes  = new byte[pxSquare * pxSquare * 4];
        float centre = pxSquare / 2f;

        for (int y = 0; y < pxSquare; y++)
        {
            float dy = y + 0.5f - centre;
            for (int x = 0; x < pxSquare; x++)
            {
                float dx = x + 0.5f - centre;

                // atan2 yields [-π, π]; shift to [0, 2π) for positive
                // angle space matching the polygonal path's convention.
                float ang = MathF.Atan2(dy, dx);
                if (ang < 0) ang += MathF.Tau;

                // Mirror collapses the second branch (ang ∈ [π, 2π))
                // onto the first [0, π) so a single alpha profile
                // computation covers both arcs. The polygonal path used
                // a branch loop with identical alpha profiles — the
                // collapse here is the per-pixel equivalent.
                if (mirror && ang >= MathF.PI)
                    ang -= MathF.PI;

                float alpha;
                if (ang >= spanRadians)
                {
                    alpha = 0f;
                }
                else if (leadFadeRadians > 0f && ang < leadFadeRadians)
                {
                    // Leading ramp: 0 at a=0, 1 at a=LeadFade.
                    float t = ang / leadFadeRadians;
                    alpha = MathF.Pow(t, curve);
                }
                else if (tailFadeRadians > 0f && ang >= tailStartRadians)
                {
                    // Trailing ramp: 1 at a=Span-TailFade, 0 at a=Span.
                    float t = (ang - tailStartRadians) / tailFadeRadians;
                    alpha = MathF.Pow(1f - t, curve);
                }
                else
                {
                    alpha = 1f;
                }

                byte a = (byte)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f);
                int idx = (y * pxSquare + x) * 4;

                // Premultiplied BGRA: a mask with fill colour (R, G, B) at
                // coverage α stores as (B·α/255, G·α/255, R·α/255, α).
                // AlphaMaskEffect downstream reads .A as the mask value;
                // RGB is invisible to the shipping stroke's masking stage
                // but matters for the playground's ArcMask / Combined naked
                // rails where the user sees the surface directly — hence
                // the theme-driven fillColor parameter.
                bytes[idx + 0] = (byte)((fillColor.B * a) / 255);
                bytes[idx + 1] = (byte)((fillColor.G * a) / 255);
                bytes[idx + 2] = (byte)((fillColor.R * a) / 255);
                bytes[idx + 3] = a;
            }
        }

        using var bitmap = CanvasBitmap.CreateFromBytes(
            canvasDevice, bytes, pxSquare, pxSquare,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        using var ds = CanvasComposition.CreateDrawingSession(surface);
        ds.Clear(Colors.Transparent);
        ds.DrawImage(bitmap);

        return surface;
    }
}
